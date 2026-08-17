namespace DigitalOps.API.Features.Attachments;

public readonly record struct TextExtractionResult(
    ExtractionStatus Status,
    string? ExtractedText,
    string? Error)
{
    public static TextExtractionResult Succeeded(string text) =>
        new(ExtractionStatus.Succeeded, text ?? string.Empty, null);

    public static TextExtractionResult Failed(string error) =>
        new(ExtractionStatus.Failed, null, error);

    public static TextExtractionResult Unsupported() =>
        new(ExtractionStatus.Unsupported, null, null);
}

public interface ITextExtractor
{
    bool CanExtract(string extension);

    Task<TextExtractionResult> ExtractAsync(
        Stream stream,
        CancellationToken cancellationToken);
}
