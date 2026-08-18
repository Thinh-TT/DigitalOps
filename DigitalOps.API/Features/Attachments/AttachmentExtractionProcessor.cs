using DigitalOps.API.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace DigitalOps.API.Features.Attachments;

public sealed class AttachmentExtractionProcessor(
    DigitalOpsDbContext dbContext,
    IAttachmentStorage storage,
    IAttachmentTextExtractor textExtractor,
    TimeProvider timeProvider,
    ILogger<AttachmentExtractionProcessor> logger) : IAttachmentExtractionProcessor
{
    public async Task<TextExtractionBatchResult> ProcessPendingAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        if (batchSize <= 0)
        {
            batchSize = 10;
        }

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var staleProcessingThreshold = utcNow.AddMinutes(-15);

        var candidateIds = await dbContext.Attachments
            .Where(item => item.ExtractionStatus == ExtractionStatus.Pending
                || (item.ExtractionStatus == ExtractionStatus.Processing
                    && item.UpdatedAt < staleProcessingThreshold))
            .OrderBy(item => item.UploadedAt)
            .Take(batchSize)
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);

        if (candidateIds.Count == 0)
        {
            return new TextExtractionBatchResult(0, 0, 0, 0);
        }

        var succeeded = 0;
        var failed = 0;
        var unsupported = 0;

        var processed = 0;
        foreach (var attachmentId in candidateIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Claim with a conditional UPDATE so two worker instances cannot process
            // the same attachment merely because they selected it in the same poll.
            var claimed = await dbContext.Attachments
                .Where(item => item.Id == attachmentId
                    && (item.ExtractionStatus == ExtractionStatus.Pending
                        || (item.ExtractionStatus == ExtractionStatus.Processing
                            && item.UpdatedAt < staleProcessingThreshold)))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.ExtractionStatus, ExtractionStatus.Processing)
                    .SetProperty(item => item.UpdatedAt, utcNow), cancellationToken);

            if (claimed != 1)
            {
                continue;
            }

            var attachment = await dbContext.Attachments
                .SingleOrDefaultAsync(item => item.Id == attachmentId, cancellationToken);
            if (attachment is null)
            {
                continue;
            }

            processed++;

            try
            {
                var storedFile = await storage.OpenReadAsync(
                    attachment.StorageKey,
                    cancellationToken);

                if (storedFile is null)
                {
                    logger.LogWarning(
                        "File storage stream missing for attachment {AttachmentId}.",
                        attachment.Id);

                    attachment.ExtractionStatus = ExtractionStatus.Failed;
                    attachment.ExtractionError = "Không thể đọc tệp từ bộ lưu trữ.";
                    attachment.ExtractedAt = utcNow;
                    attachment.UpdatedAt = utcNow;
                    failed++;
                }
                else
                {
                    await using var content = storedFile.Content;
                    var result = await textExtractor.ExtractAsync(
                        attachment.FileName,
                        content,
                        cancellationToken);

                    attachment.ExtractionStatus = result.Status;
                    attachment.ExtractedText = result.ExtractedText;
                    attachment.ExtractionError = result.Error;
                    attachment.ExtractedAt = utcNow;
                    attachment.UpdatedAt = utcNow;

                    switch (result.Status)
                    {
                        case ExtractionStatus.Succeeded:
                            succeeded++;
                            break;
                        case ExtractionStatus.Failed:
                            failed++;
                            break;
                        case ExtractionStatus.Unsupported:
                            unsupported++;
                            break;
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(
                    exception,
                    "Text extraction failed unexpectedly for attachment {AttachmentId}.",
                    attachment.Id);

                attachment.ExtractionStatus = ExtractionStatus.Failed;
                attachment.ExtractionError = "Lỗi không xác định khi trích xuất tệp.";
                attachment.ExtractedAt = utcNow;
                attachment.UpdatedAt = utcNow;
                failed++;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new TextExtractionBatchResult(
            processed,
            succeeded,
            failed,
            unsupported);
    }
}
