namespace DigitalOps.API.Features.Attachments;

public readonly record struct TextExtractionBatchResult(
    int ProcessedCount,
    int SucceededCount,
    int FailedCount,
    int UnsupportedCount);

public interface IAttachmentExtractionProcessor
{
    Task<TextExtractionBatchResult> ProcessPendingAsync(
        int batchSize,
        CancellationToken cancellationToken);
}
