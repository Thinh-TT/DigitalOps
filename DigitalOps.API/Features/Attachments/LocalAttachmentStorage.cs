using Microsoft.Extensions.Options;

namespace DigitalOps.API.Features.Attachments;

public sealed class LocalAttachmentStorage : IAttachmentStorage
{
    private readonly string _rootPath;
    private readonly string _rootPrefix;

    public LocalAttachmentStorage(
        IOptions<AttachmentStorageOptions> options,
        IWebHostEnvironment environment)
    {
        _rootPath = AttachmentStorageOptionsValidator.ResolvePath(
            environment.ContentRootPath,
            options.Value.RootPath);
        _rootPrefix = _rootPath.TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
    }

    public async Task WriteAsync(
        string storageKey,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var finalPath = ResolveStoragePath(storageKey);
        var stagingDirectory = ResolveStoragePath(".staging");
        Directory.CreateDirectory(stagingDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        var temporaryPath = Path.Combine(stagingDirectory, $"{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await content.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, finalPath, overwrite: false);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException
            and not OutOfMemoryException)
        {
            TryDelete(temporaryPath);
            throw new AttachmentStorageException(
                "The attachment could not be written to local storage.",
                exception);
        }
    }

    public Task<StoredAttachmentFile?> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolveStoragePath(storageKey);
        if (!File.Exists(path))
        {
            return Task.FromResult<StoredAttachmentFile?>(null);
        }

        try
        {
            var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return Task.FromResult<StoredAttachmentFile?>(
                new StoredAttachmentFile(stream, stream.Length));
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException
            and not OutOfMemoryException)
        {
            throw new AttachmentStorageException(
                "The attachment could not be opened from local storage.",
                exception);
        }
    }

    public Task<IAttachmentDeleteOperation?> StageDeleteAsync(
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sourcePath = ResolveStoragePath(storageKey);
        if (!File.Exists(sourcePath))
        {
            return Task.FromResult<IAttachmentDeleteOperation?>(null);
        }

        var trashDirectory = ResolveStoragePath(".trash");
        Directory.CreateDirectory(trashDirectory);
        var trashPath = Path.Combine(trashDirectory, $"{Guid.NewGuid():N}.deleted");

        try
        {
            File.Move(sourcePath, trashPath, overwrite: false);
            return Task.FromResult<IAttachmentDeleteOperation?>(
                new LocalDeleteOperation(sourcePath, trashPath));
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException
            and not OutOfMemoryException)
        {
            throw new AttachmentStorageException(
                "The attachment could not be staged for deletion.",
                exception);
        }
    }

    private string ResolveStoragePath(string storageKey)
    {
        var normalizedKey = storageKey.Replace('/', Path.DirectorySeparatorChar);
        var path = Path.GetFullPath(Path.Combine(_rootPath, normalizedKey));
        if (!path.StartsWith(_rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new AttachmentStorageException("The attachment storage key is invalid.");
        }

        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // The original storage failure is more useful than cleanup failure here.
        }
    }

    private sealed class LocalDeleteOperation(
        string sourcePath,
        string trashPath) : IAttachmentDeleteOperation
    {
        private bool _completed;

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                File.Delete(trashPath);
                _completed = true;
                return Task.CompletedTask;
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException
                and not OutOfMemoryException)
            {
                _completed = true;
                throw new AttachmentStorageException(
                    "The staged attachment could not be purged.",
                    exception);
            }
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
                File.Move(trashPath, sourcePath, overwrite: false);
                _completed = true;
                return Task.CompletedTask;
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException
                and not OutOfMemoryException)
            {
                _completed = true;
                throw new AttachmentStorageException(
                    "The staged attachment could not be restored.",
                    exception);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_completed || !File.Exists(trashPath))
            {
                return;
            }

            await RollbackAsync();
        }
    }
}
