using Microsoft.Extensions.Options;

namespace DigitalOps.API.Features.Attachments;

public interface IAttachmentTextExtractor
{
    Task<TextExtractionResult> ExtractAsync(
        string fileName,
        Stream stream,
        CancellationToken cancellationToken);
}

public sealed class AttachmentTextExtractor : IAttachmentTextExtractor
{
    private readonly IReadOnlyList<ITextExtractor> _extractors;

    public AttachmentTextExtractor(
        IEnumerable<ITextExtractor>? extractors = null,
        IOptions<TextExtractionWorkerOptions>? options = null)
    {
        var configuredExtractors = extractors?.ToList();
        if (configuredExtractors is { Count: > 0 })
        {
            _extractors = configuredExtractors;
            return;
        }

        var extractionOptions = options?.Value ?? new TextExtractionWorkerOptions();
        _extractors = new List<ITextExtractor>
        {
            new PdfTextExtractor(extractionOptions),
            new DocxTextExtractor(extractionOptions),
            new XlsxTextExtractor(extractionOptions)
        };
    }

    public async Task<TextExtractionResult> ExtractAsync(
        string fileName,
        Stream stream,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var extractor = _extractors.FirstOrDefault(item => item.CanExtract(extension));

        if (extractor is null)
        {
            return TextExtractionResult.Unsupported();
        }

        return await extractor.ExtractAsync(stream, cancellationToken);
    }
}
