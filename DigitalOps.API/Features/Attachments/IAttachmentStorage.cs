namespace DigitalOps.API.Features.Attachments;

public interface IAttachmentStorage
{
    Task WriteAsync(
        string storageKey,
        Stream content,
        CancellationToken cancellationToken = default);

    Task<StoredAttachmentFile?> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken = default);

    Task<IAttachmentDeleteOperation?> StageDeleteAsync(
        string storageKey,
        CancellationToken cancellationToken = default);
}

public sealed record StoredAttachmentFile(Stream Content, long Length);

public interface IAttachmentDeleteOperation : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);

    Task RollbackAsync(CancellationToken cancellationToken = default);
}

public sealed class AttachmentStorageException(
    string message,
    Exception? innerException = null) : Exception(message, innerException);
