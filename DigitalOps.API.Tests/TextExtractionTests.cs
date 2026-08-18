using System.IO.Compression;
using System.Text;
using ClosedXML.Excel;
using DigitalOps.API.Features.Attachments;
using DigitalOps.API.Shared.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace DigitalOps.API.Tests;

public sealed class TextExtractionTests
{
    [Fact]
    public async Task DocxTextExtractor_extracts_paragraphs_and_text()
    {
        var docxStream = CreateDocxStream("Báo cáo công tác tuần 12", "UBND Phường Phước Long B");
        var extractor = new DocxTextExtractor();

        var result = await extractor.ExtractAsync(docxStream, CancellationToken.None);

        Assert.Equal(ExtractionStatus.Succeeded, result.Status);
        Assert.NotNull(result.ExtractedText);
        Assert.Contains("Báo cáo công tác tuần 12", result.ExtractedText);
        Assert.Contains("UBND Phường Phước Long B", result.ExtractedText);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task XlsxTextExtractor_extracts_worksheet_cells()
    {
        var xlsxStream = CreateXlsxStream("Danh sách hội viên", "Nguyễn Văn A", "0901234567");
        var extractor = new XlsxTextExtractor();

        var result = await extractor.ExtractAsync(xlsxStream, CancellationToken.None);

        Assert.Equal(ExtractionStatus.Succeeded, result.Status);
        Assert.NotNull(result.ExtractedText);
        Assert.Contains("Danh sách hội viên", result.ExtractedText);
        Assert.Contains("Nguyễn Văn A", result.ExtractedText);
        Assert.Contains("0901234567", result.ExtractedText);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task PdfTextExtractor_extracts_text_layer()
    {
        var pdfStream = CreatePdfStream("CONG HOA XA HOI CHU NGHIA VIET NAM");
        var extractor = new PdfTextExtractor();

        var result = await extractor.ExtractAsync(pdfStream, CancellationToken.None);

        Assert.Equal(ExtractionStatus.Succeeded, result.Status);
        Assert.NotNull(result.ExtractedText);
        Assert.Contains("CONG HOA XA HOI CHU NGHIA VIET NAM", result.ExtractedText);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task PdfTextExtractor_marks_pdf_without_text_layer_as_unsupported()
    {
        var extractor = new PdfTextExtractor();

        var result = await extractor.ExtractAsync(CreateBlankPdfStream(), CancellationToken.None);

        Assert.Equal(ExtractionStatus.Unsupported, result.Status);
        Assert.Null(result.ExtractedText);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task DocxTextExtractor_rejects_text_over_configured_limit()
    {
        var extractor = new DocxTextExtractor(new TextExtractionWorkerOptions
        {
            MaxExtractedTextChars = 8
        });

        var result = await extractor.ExtractAsync(
            CreateDocxStream("Nội dung vượt giới hạn"),
            CancellationToken.None);

        Assert.Equal(ExtractionStatus.Failed, result.Status);
        Assert.Null(result.ExtractedText);
        Assert.Equal("Lỗi trích xuất DOCX.", result.Error);
    }

    [Fact]
    public async Task AttachmentTextExtractor_routes_supported_and_unsupported_extensions()
    {
        var extractor = new AttachmentTextExtractor();

        var pdfResult = await extractor.ExtractAsync(
            "document.pdf",
            CreatePdfStream("Test PDF"),
            CancellationToken.None);
        var pngResult = await extractor.ExtractAsync(
            "image.png",
            new MemoryStream([0x89, 0x50, 0x4E, 0x47]),
            CancellationToken.None);

        Assert.Equal(ExtractionStatus.Succeeded, pdfResult.Status);
        Assert.Equal(ExtractionStatus.Unsupported, pngResult.Status);
    }

    [Fact]
    public async Task ProcessPendingAsync_processes_pending_attachments_and_updates_database()
    {
        await using var database = await AttachmentTestDatabase.CreateAsync();
        var data = await database.CreateIncomingAsync();

        var docxStream = CreateDocxStream("Văn bản đến số 105", "Trích yếu công văn");
        var uploadResult = await database.Service.UploadIncomingAsync(
            data.Document.Id,
            data.Staff.Id,
            docxStream,
            "congvan.docx",
            docxStream.Length);

        Assert.True(uploadResult.Succeeded);
        Assert.Equal(ExtractionStatus.Pending, uploadResult.Value!.ExtractionStatus);

        var processor = new AttachmentExtractionProcessor(
            database.Context,
            database.Storage,
            new AttachmentTextExtractor(),
            TimeProvider.System,
            NullLogger<AttachmentExtractionProcessor>.Instance);

        var batchResult = await processor.ProcessPendingAsync(10, CancellationToken.None);

        Assert.Equal(1, batchResult.ProcessedCount);
        Assert.Equal(1, batchResult.SucceededCount);
        Assert.Equal(0, batchResult.FailedCount);

        var updatedAttachment = await database.Context.Attachments
            .SingleAsync(item => item.Id == uploadResult.Value.Id);

        Assert.Equal(ExtractionStatus.Succeeded, updatedAttachment.ExtractionStatus);
        Assert.NotNull(updatedAttachment.ExtractedText);
        Assert.Contains("Văn bản đến số 105", updatedAttachment.ExtractedText);
        Assert.NotNull(updatedAttachment.ExtractedAt);
        Assert.Null(updatedAttachment.ExtractionError);
    }

    [Fact]
    public async Task ProcessPendingAsync_is_idempotent_when_no_pending_attachments()
    {
        await using var database = await AttachmentTestDatabase.CreateAsync();

        var processor = new AttachmentExtractionProcessor(
            database.Context,
            database.Storage,
            new AttachmentTextExtractor(),
            TimeProvider.System,
            NullLogger<AttachmentExtractionProcessor>.Instance);

        var batchResult = await processor.ProcessPendingAsync(10, CancellationToken.None);

        Assert.Equal(0, batchResult.ProcessedCount);
        Assert.Equal(0, batchResult.SucceededCount);
        Assert.Equal(0, batchResult.FailedCount);
        Assert.Equal(0, batchResult.UnsupportedCount);
    }

    [Fact]
    public async Task ProcessPendingAsync_handles_failed_storage_or_corrupt_files()
    {
        await using var database = await AttachmentTestDatabase.CreateAsync();
        var data = await database.CreateIncomingAsync();

        var attachment = new Attachment
        {
            Id = Guid.NewGuid(),
            IncomingDocumentId = data.Document.Id,
            StorageKey = "incoming/missing/nonexistent.docx",
            FileName = "nonexistent.docx",
            UploadedByStaffId = data.Staff.Id,
            ExtractionStatus = ExtractionStatus.Pending,
            UploadedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        database.Context.Attachments.Add(attachment);
        await database.Context.SaveChangesAsync();

        var processor = new AttachmentExtractionProcessor(
            database.Context,
            database.Storage,
            new AttachmentTextExtractor(),
            TimeProvider.System,
            NullLogger<AttachmentExtractionProcessor>.Instance);

        var batchResult = await processor.ProcessPendingAsync(10, CancellationToken.None);

        Assert.Equal(1, batchResult.ProcessedCount);
        Assert.Equal(1, batchResult.FailedCount);

        var failedAttachment = await database.Context.Attachments
            .SingleAsync(item => item.Id == attachment.Id);

        Assert.Equal(ExtractionStatus.Failed, failedAttachment.ExtractionStatus);
        Assert.NotNull(failedAttachment.ExtractionError);
        Assert.Null(failedAttachment.ExtractedText);
    }

    private static MemoryStream CreateDocxStream(params string[] paragraphs)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var contentTypesEntry = archive.CreateEntry("[Content_Types].xml");
            using (var ctStream = contentTypesEntry.Open())
            using (var ctWriter = new StreamWriter(ctStream, Encoding.UTF8))
            {
                ctWriter.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"></Types>");
            }

            var entry = archive.CreateEntry("word/document.xml");
            using var entryStream = entry.Open();
            using var writer = new StreamWriter(entryStream, Encoding.UTF8);

            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">");
            sb.Append("<w:body>");
            foreach (var p in paragraphs)
            {
                sb.Append($"<w:p><w:r><w:t>{p}</w:t></w:r></w:p>");
            }
            sb.Append("</w:body></w:document>");

            writer.Write(sb.ToString());
        }

        stream.Position = 0;
        return stream;
    }

    private static MemoryStream CreateXlsxStream(string sheetName, params string[] cellValues)
    {
        var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add(sheetName);
            for (var i = 0; i < cellValues.Length; i++)
            {
                sheet.Cell(i + 1, 1).Value = cellValues[i];
            }

            workbook.SaveAs(stream);
        }

        stream.Position = 0;
        return stream;
    }

    private static MemoryStream CreatePdfStream(string text)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        page.AddText(text, 12, new UglyToad.PdfPig.Core.PdfPoint(50, 700), font);

        var bytes = builder.Build();
        return new MemoryStream(bytes);
    }

    private static MemoryStream CreateBlankPdfStream()
    {
        var builder = new PdfDocumentBuilder();
        builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        return new MemoryStream(builder.Build());
    }
}
