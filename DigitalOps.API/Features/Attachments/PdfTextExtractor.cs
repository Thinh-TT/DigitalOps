using System.Text;
using UglyToad.PdfPig;

namespace DigitalOps.API.Features.Attachments;

public sealed class PdfTextExtractor : ITextExtractor
{
    private readonly TextExtractionWorkerOptions _options;

    public PdfTextExtractor(TextExtractionWorkerOptions? options = null)
    {
        _options = options ?? new TextExtractionWorkerOptions();
    }

    public bool CanExtract(string extension) =>
        string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase);

    public Task<TextExtractionResult> ExtractAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        try
        {
            stream.Position = 0;
            using var pdfDocument = PdfDocument.Open(stream);
            var builder = new StringBuilder();

            var pageNumber = 0;
            foreach (var page in pdfDocument.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                pageNumber++;
                if (pageNumber > _options.MaxPdfPages)
                {
                    return Task.FromResult(
                        TextExtractionResult.Failed("PDF vượt giới hạn số trang được phép trích xuất."));
                }

                var pageText = page.Text;
                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    if (!TextExtractionGuard.TryAppendLine(builder, pageText.Trim(), _options.MaxExtractedTextChars))
                    {
                        return Task.FromResult(
                            TextExtractionResult.Failed("PDF vượt giới hạn dung lượng văn bản được phép trích xuất."));
                    }
                }
            }

            var resultText = builder.ToString().Trim();
            return Task.FromResult(string.IsNullOrWhiteSpace(resultText)
                ? TextExtractionResult.Unsupported()
                : TextExtractionResult.Succeeded(resultText));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Task.FromResult(
                TextExtractionResult.Failed("Lỗi trích xuất PDF."));
        }
    }
}
