using System.IO.Compression;
using System.Text;
using System.Xml;

namespace DigitalOps.API.Features.Attachments;

public sealed class DocxTextExtractor : ITextExtractor
{
    private readonly TextExtractionWorkerOptions _options;

    public DocxTextExtractor(TextExtractionWorkerOptions? options = null)
    {
        _options = options ?? new TextExtractionWorkerOptions();
    }

    public bool CanExtract(string extension) =>
        string.Equals(extension, ".docx", StringComparison.OrdinalIgnoreCase);

    public Task<TextExtractionResult> ExtractAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        try
        {
            stream.Position = 0;
            var packageError = TextExtractionGuard.ValidateZipPackage(stream, _options);
            if (packageError is not null)
            {
                return Task.FromResult(TextExtractionResult.Failed(packageError));
            }

            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var entry = archive.GetEntry("word/document.xml");
            if (entry is null)
            {
                return Task.FromResult(
                    TextExtractionResult.Failed("Không tìm thấy word/document.xml trong tệp DOCX."));
            }

            using var entryStream = entry.Open();
            using var xmlReader = XmlReader.Create(entryStream, new XmlReaderSettings
            {
                IgnoreComments = true,
                IgnoreWhitespace = false,
                DtdProcessing = DtdProcessing.Prohibit,
                MaxCharactersInDocument = _options.MaxExtractedTextChars
            });

            var builder = new StringBuilder();
            var lastWasParagraphEnd = false;

            while (xmlReader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (xmlReader.NodeType == XmlNodeType.Element)
                {
                    if (xmlReader.LocalName == "p")
                    {
                        if (builder.Length > 0 && !lastWasParagraphEnd)
                        {
                            builder.AppendLine();
                            lastWasParagraphEnd = true;
                        }
                    }
                    else if (xmlReader.LocalName == "br")
                    {
                        builder.AppendLine();
                        lastWasParagraphEnd = true;
                    }
                    else if (xmlReader.LocalName == "t")
                    {
                        var text = xmlReader.ReadElementContentAsString();
                        if (!string.IsNullOrEmpty(text))
                        {
                            if (!TextExtractionGuard.TryAppend(builder, text, _options.MaxExtractedTextChars))
                            {
                                return Task.FromResult(
                                    TextExtractionResult.Failed("DOCX vượt giới hạn dung lượng văn bản được phép trích xuất."));
                            }
                            lastWasParagraphEnd = false;
                        }
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
                TextExtractionResult.Failed("Lỗi trích xuất DOCX."));
        }
    }
}
