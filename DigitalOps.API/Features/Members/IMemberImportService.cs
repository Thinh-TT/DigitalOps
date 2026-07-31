namespace DigitalOps.API.Features.Members;

public interface IMemberImportService
{
    byte[] CreateTemplate();

    Task<MemberImportServiceResult> ImportAsync(
        Stream stream,
        string fileName,
        long fileLength,
        CancellationToken cancellationToken = default);
}

public enum MemberImportFailure
{
    None,
    PayloadTooLarge,
    UnsupportedFileType,
    Validation
}

public sealed record MemberImportServiceResult(
    MemberImportResult? Result,
    MemberImportFailure Failure,
    string? Detail)
{
    public bool Succeeded => Failure == MemberImportFailure.None;

    public static MemberImportServiceResult Success(MemberImportResult result) =>
        new(result, MemberImportFailure.None, null);

    public static MemberImportServiceResult PayloadTooLarge(string detail) =>
        new(null, MemberImportFailure.PayloadTooLarge, detail);

    public static MemberImportServiceResult UnsupportedFileType(string detail) =>
        new(null, MemberImportFailure.UnsupportedFileType, detail);

    public static MemberImportServiceResult Validation(
        int totalRows,
        IReadOnlyList<MemberImportRowError> errors) =>
        new(
            new MemberImportResult(0, totalRows, errors),
            MemberImportFailure.Validation,
            "Tệp import có lỗi. Không có hội viên nào được nhập.");
}
