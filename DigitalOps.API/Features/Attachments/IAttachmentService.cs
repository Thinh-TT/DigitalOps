namespace DigitalOps.API.Features.Attachments;

public interface IAttachmentService
{
    Task<AttachmentResult<AttachmentResponse>> UploadIncomingAsync(
        Guid incomingDocumentId,
        Guid uploadedByStaffId,
        Stream content,
        string fileName,
        long fileLength,
        CancellationToken cancellationToken = default);

    Task<AttachmentResult<AttachmentDownload>> DownloadAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<AttachmentResult<bool>> DeleteIncomingAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}

public enum AttachmentFailure
{
    None,
    Validation,
    NotFound,
    Conflict,
    PayloadTooLarge,
    UnsupportedFileType,
    Storage
}

public sealed record AttachmentResult<T>(
    T? Value,
    AttachmentFailure Failure,
    string? Detail,
    IReadOnlyDictionary<string, string[]> Errors)
{
    public bool Succeeded => Failure == AttachmentFailure.None;

    public static AttachmentResult<T> Success(T value) =>
        new(value, AttachmentFailure.None, null, EmptyErrors());

    public static AttachmentResult<T> Validation(
        IReadOnlyDictionary<string, string[]> errors) =>
        new(default, AttachmentFailure.Validation, null, errors);

    public static AttachmentResult<T> NotFound() =>
        new(default, AttachmentFailure.NotFound, null, EmptyErrors());

    public static AttachmentResult<T> Conflict(string detail) =>
        new(default, AttachmentFailure.Conflict, detail, EmptyErrors());

    public static AttachmentResult<T> PayloadTooLarge(string detail) =>
        new(default, AttachmentFailure.PayloadTooLarge, detail, EmptyErrors());

    public static AttachmentResult<T> UnsupportedFileType(string detail) =>
        new(default, AttachmentFailure.UnsupportedFileType, detail, EmptyErrors());

    public static AttachmentResult<T> Storage() =>
        new(
            default,
            AttachmentFailure.Storage,
            "Không thể truy cập kho lưu trữ file.",
            EmptyErrors());

    private static IReadOnlyDictionary<string, string[]> EmptyErrors() =>
        new Dictionary<string, string[]>();
}
