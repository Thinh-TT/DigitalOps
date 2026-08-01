using DigitalOps.API.Features.IncomingDocuments;
using DigitalOps.API.Features.OutgoingDocuments;
using DigitalOps.API.Shared.Data;
using DigitalOps.API.Shared.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DigitalOps.API.Features.Attachments;

public sealed class AttachmentService(
    DigitalOpsDbContext dbContext,
    IAttachmentStorage storage,
    IOptions<AttachmentStorageOptions> options,
    TimeProvider timeProvider,
    ILogger<AttachmentService> logger) : IAttachmentService
{
    public async Task<AttachmentResult<AttachmentResponse>> UploadIncomingAsync(
        Guid incomingDocumentId,
        Guid uploadedByStaffId,
        Stream content,
        string fileName,
        long fileLength,
        CancellationToken cancellationToken = default)
    {
        var document = await dbContext.IncomingDocuments.SingleOrDefaultAsync(
            item => item.Id == incomingDocumentId,
            cancellationToken);
        if (document is null)
        {
            return AttachmentResult<AttachmentResponse>.NotFound();
        }

        if (document.Status == IncomingDocumentStatus.Completed)
        {
            return AttachmentResult<AttachmentResponse>.Conflict(
                "Văn bản đến đã hoàn tất và không thể thêm file đính kèm.");
        }

        var uploader = await dbContext.Staff.SingleOrDefaultAsync(
            staff => staff.Id == uploadedByStaffId && staff.IsActive,
            cancellationToken);
        if (uploader is null)
        {
            return AttachmentResult<AttachmentResponse>.NotFound();
        }

        return await UploadToParentAsync(
            content,
            fileName,
            fileLength,
            uploader,
            incomingDocumentId,
            null,
            document,
            null,
            cancellationToken);
    }

    public async Task<AttachmentResult<AttachmentResponse>> UploadOutgoingAsync(
        Guid outgoingDocumentId,
        Guid uploadedByStaffId,
        Stream content,
        string fileName,
        long fileLength,
        CancellationToken cancellationToken = default)
    {
        var document = await dbContext.OutgoingDocuments.SingleOrDefaultAsync(
            item => item.Id == outgoingDocumentId,
            cancellationToken);
        if (document is null)
        {
            return AttachmentResult<AttachmentResponse>.NotFound();
        }

        if (document.Status is not (
            OutgoingDocumentStatus.AiDraft
            or OutgoingDocumentStatus.Editing
            or OutgoingDocumentStatus.ReviewFailed))
        {
            return AttachmentResult<AttachmentResponse>.Conflict(
                "Văn bản đi hiện không cho phép thay đổi file đính kèm.");
        }

        if (document.DraftedByStaffId != uploadedByStaffId)
        {
            return AttachmentResult<AttachmentResponse>.Forbidden(
                "Chỉ cán bộ soạn văn bản mới được quản lý file đính kèm.");
        }

        var uploader = await dbContext.Staff.SingleOrDefaultAsync(
            staff => staff.Id == uploadedByStaffId && staff.IsActive,
            cancellationToken);
        if (uploader is null)
        {
            return AttachmentResult<AttachmentResponse>.NotFound();
        }

        return await UploadToParentAsync(
            content,
            fileName,
            fileLength,
            uploader,
            null,
            outgoingDocumentId,
            null,
            document,
            cancellationToken);
    }

    public async Task<AttachmentResult<AttachmentDownload>> DownloadAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var attachment = await dbContext.Attachments
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (attachment is null)
        {
            return AttachmentResult<AttachmentDownload>.NotFound();
        }

        try
        {
            var storedFile = await storage.OpenReadAsync(
                attachment.StorageKey,
                cancellationToken);
            if (storedFile is null)
            {
                logger.LogError(
                    "Attachment object is missing for attachment {AttachmentId}.",
                    attachment.Id);
                return AttachmentResult<AttachmentDownload>.NotFound();
            }

            return AttachmentResult<AttachmentDownload>.Success(
                new AttachmentDownload(
                    storedFile.Content,
                    attachment.FileName,
                    AttachmentFileValidator.GetContentType(attachment.FileName)));
        }
        catch (AttachmentStorageException exception)
        {
            logger.LogError(
                exception,
                "Attachment storage read failed for attachment {AttachmentId}.",
                attachment.Id);
            return AttachmentResult<AttachmentDownload>.Storage();
        }
    }

    public async Task<AttachmentResult<bool>> DeleteIncomingAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var attachment = await dbContext.Attachments
            .Include(item => item.IncomingDocument)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (attachment is null)
        {
            return AttachmentResult<bool>.NotFound();
        }

        if (attachment.IncomingDocument is null)
        {
            return AttachmentResult<bool>.NotFound();
        }

        if (attachment.IncomingDocument.Status == IncomingDocumentStatus.Completed)
        {
            return AttachmentResult<bool>.Conflict(
                "Văn bản đến đã hoàn tất và không thể xóa file đính kèm.");
        }

        return await DeleteStoredAttachmentAsync(attachment, cancellationToken);
    }

    public async Task<AttachmentResult<bool>> DeleteAsync(
        Guid id,
        Guid callerStaffId,
        bool callerIsClerk,
        bool callerIsDrafter,
        CancellationToken cancellationToken = default)
    {
        var attachment = await dbContext.Attachments
            .Include(item => item.IncomingDocument)
            .Include(item => item.OutgoingDocument)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (attachment is null)
        {
            return AttachmentResult<bool>.NotFound();
        }

        if (attachment.IncomingDocument is not null)
        {
            if (!callerIsClerk)
            {
                return AttachmentResult<bool>.Forbidden(
                    "Chỉ Văn thư được xóa file đính kèm văn bản đến.");
            }

            if (attachment.IncomingDocument.Status == IncomingDocumentStatus.Completed)
            {
                return AttachmentResult<bool>.Conflict(
                    "Văn bản đến đã hoàn tất và không thể xóa file đính kèm.");
            }
        }
        else if (attachment.OutgoingDocument is not null)
        {
            if (!callerIsDrafter
                || attachment.OutgoingDocument.DraftedByStaffId != callerStaffId)
            {
                return AttachmentResult<bool>.Forbidden(
                    "Chỉ cán bộ soạn văn bản mới được xóa file đính kèm.");
            }

            if (attachment.OutgoingDocument.Status is not (
                OutgoingDocumentStatus.AiDraft
                or OutgoingDocumentStatus.Editing
                or OutgoingDocumentStatus.ReviewFailed))
            {
                return AttachmentResult<bool>.Conflict(
                    "Văn bản đi hiện không cho phép thay đổi file đính kèm.");
            }
        }
        else
        {
            return AttachmentResult<bool>.NotFound();
        }

        return await DeleteStoredAttachmentAsync(attachment, cancellationToken);
    }

    private async Task<AttachmentResult<AttachmentResponse>> UploadToParentAsync(
        Stream content,
        string fileName,
        long fileLength,
        Staff uploader,
        Guid? incomingDocumentId,
        Guid? outgoingDocumentId,
        IncomingDocument? incomingDocument,
        OutgoingDocument? outgoingDocument,
        CancellationToken cancellationToken)
    {
        var validation = await AttachmentFileValidator.ValidateAsync(
            content,
            fileName,
            fileLength,
            options.Value.MaxFileSizeBytes,
            cancellationToken);
        if (!validation.Succeeded)
        {
            return FromValidation<AttachmentResponse>(validation);
        }

        var id = Guid.NewGuid();
        var parentKind = incomingDocumentId is not null ? "incoming" : "outgoing";
        var parentId = incomingDocumentId ?? outgoingDocumentId!.Value;
        var storageKey = $"{parentKind}/{parentId:N}/{id:N}{validation.Extension}";
        try
        {
            await storage.WriteAsync(storageKey, content, cancellationToken);
        }
        catch (AttachmentStorageException exception)
        {
            logger.LogError(
                exception,
                "Attachment storage write failed for attachment {AttachmentId}.",
                id);
            return AttachmentResult<AttachmentResponse>.Storage();
        }

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var attachment = new Attachment
        {
            Id = id,
            IncomingDocumentId = incomingDocumentId,
            IncomingDocument = incomingDocument,
            OutgoingDocumentId = outgoingDocumentId,
            OutgoingDocument = outgoingDocument,
            StorageKey = storageKey,
            FileName = validation.FileName!,
            UploadedByStaffId = uploader.Id,
            UploadedByStaff = uploader,
            ExtractionStatus = validation.ExtractionStatus,
            UploadedAt = utcNow,
            UpdatedAt = utcNow
        };

        dbContext.Attachments.Add(attachment);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await CompensateStoredFileAsync(storageKey, id);
            throw;
        }

        return AttachmentResult<AttachmentResponse>.Success(
            AttachmentMappings.ToResponse(attachment));
    }

    private async Task<AttachmentResult<bool>> DeleteStoredAttachmentAsync(
        Attachment attachment,
        CancellationToken cancellationToken)
    {
        IAttachmentDeleteOperation? deleteOperation;
        try
        {
            deleteOperation = await storage.StageDeleteAsync(
                attachment.StorageKey,
                cancellationToken);
        }
        catch (AttachmentStorageException exception)
        {
            logger.LogError(
                exception,
                "Attachment storage delete staging failed for attachment {AttachmentId}.",
                attachment.Id);
            return AttachmentResult<bool>.Storage();
        }

        if (deleteOperation is null)
        {
            logger.LogError(
                "Attachment object is missing during delete for attachment {AttachmentId}.",
                attachment.Id);
            return AttachmentResult<bool>.NotFound();
        }

        await using (deleteOperation)
        {
            dbContext.Attachments.Remove(attachment);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                try
                {
                    await deleteOperation.RollbackAsync(CancellationToken.None);
                }
                catch (AttachmentStorageException rollbackException)
                {
                    logger.LogCritical(
                        rollbackException,
                        "Attachment delete rollback failed for attachment {AttachmentId}.",
                        attachment.Id);
                }

                throw;
            }

            try
            {
                await deleteOperation.CommitAsync(CancellationToken.None);
            }
            catch (AttachmentStorageException exception)
            {
                logger.LogError(
                    exception,
                    "Attachment quarantine cleanup failed for attachment {AttachmentId}.",
                    attachment.Id);
            }
        }

        return AttachmentResult<bool>.Success(true);
    }

    private async Task CompensateStoredFileAsync(string storageKey, Guid attachmentId)
    {
        try
        {
            var operation = await storage.StageDeleteAsync(
                storageKey,
                CancellationToken.None);
            if (operation is null)
            {
                return;
            }

            await using (operation)
            {
                await operation.CommitAsync(CancellationToken.None);
            }
        }
        catch (AttachmentStorageException exception)
        {
            logger.LogCritical(
                exception,
                "Attachment upload compensation failed for attachment {AttachmentId}.",
                attachmentId);
        }
    }

    private static AttachmentResult<T> FromValidation<T>(
        AttachmentFileValidationResult validation) =>
        validation.Failure switch
        {
            AttachmentFailure.Validation =>
                AttachmentResult<T>.Validation(validation.Errors),
            AttachmentFailure.PayloadTooLarge =>
                AttachmentResult<T>.PayloadTooLarge(validation.Detail!),
            AttachmentFailure.UnsupportedFileType =>
                AttachmentResult<T>.UnsupportedFileType(validation.Detail!),
            _ => throw new InvalidOperationException(
                "The attachment validator returned an unsupported failure.")
        };
}
