using DigitalOps.API.Shared.Api;

namespace DigitalOps.API.Features.IncomingDocuments;

public interface IIncomingDocumentService
{
    Task<PagedResponse<IncomingDocumentResponse>> GetListAsync(
        IncomingDocumentListQuery query,
        CancellationToken cancellationToken = default);

    Task<IncomingDocumentResponse?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<IncomingDocumentResult<IncomingDocumentResponse>> CreateAsync(
        IncomingDocumentCreateRequest request,
        CancellationToken cancellationToken = default);

    Task<IncomingDocumentResult<IncomingDocumentResponse>> UpdateAsync(
        Guid id,
        IncomingDocumentUpdateRequest request,
        CancellationToken cancellationToken = default);

    Task<IncomingDocumentResult<IncomingDocumentResponse>> CompleteAsync(
        Guid id,
        Guid callerStaffId,
        bool callerIsClerk,
        CancellationToken cancellationToken = default);
}

public enum IncomingDocumentFailure
{
    None,
    NotFound,
    Validation,
    Conflict,
    Forbidden
}

public sealed record IncomingDocumentResult<T>(
    T? Value,
    IncomingDocumentFailure Failure,
    IReadOnlyDictionary<string, string[]> Errors,
    string? Detail)
{
    public bool Succeeded => Failure == IncomingDocumentFailure.None;

    public static IncomingDocumentResult<T> Success(T value) =>
        new(value, IncomingDocumentFailure.None, EmptyErrors, null);

    public static IncomingDocumentResult<T> NotFound() =>
        new(default, IncomingDocumentFailure.NotFound, EmptyErrors, null);

    public static IncomingDocumentResult<T> Validation(
        IReadOnlyDictionary<string, string[]> errors) =>
        new(default, IncomingDocumentFailure.Validation, errors, null);

    public static IncomingDocumentResult<T> Conflict(string detail) =>
        new(default, IncomingDocumentFailure.Conflict, EmptyErrors, detail);

    public static IncomingDocumentResult<T> Forbidden() =>
        new(default, IncomingDocumentFailure.Forbidden, EmptyErrors, null);

    private static readonly IReadOnlyDictionary<string, string[]> EmptyErrors =
        new Dictionary<string, string[]>(StringComparer.Ordinal);
}
