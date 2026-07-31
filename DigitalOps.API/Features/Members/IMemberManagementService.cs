using DigitalOps.API.Shared.Api;

namespace DigitalOps.API.Features.Members;

public interface IMemberManagementService
{
    Task<PagedResponse<MemberResponse>> GetListAsync(
        MemberListQuery query,
        CancellationToken cancellationToken = default);

    Task<PagedResponse<MemberLookupResponse>> GetLookupAsync(
        MemberLookupQuery query,
        CancellationToken cancellationToken = default);

    Task<MemberResponse?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<MemberServiceResult<MemberResponse>> CreateAsync(
        MemberUpsertRequest request,
        CancellationToken cancellationToken = default);

    Task<MemberServiceResult<MemberResponse>> UpdateAsync(
        Guid id,
        MemberUpsertRequest request,
        CancellationToken cancellationToken = default);

    Task<MemberServiceResult<MemberResponse>> DeactivateAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}

public enum MemberServiceFailure
{
    None,
    NotFound,
    Validation,
    Conflict
}

public sealed record MemberServiceResult<T>(
    T? Value,
    MemberServiceFailure Failure,
    IReadOnlyDictionary<string, string[]> Errors,
    string? Detail)
{
    public bool Succeeded => Failure == MemberServiceFailure.None;

    public static MemberServiceResult<T> Success(T value) =>
        new(value, MemberServiceFailure.None, EmptyErrors, null);

    public static MemberServiceResult<T> NotFound() =>
        new(default, MemberServiceFailure.NotFound, EmptyErrors, null);

    public static MemberServiceResult<T> Validation(
        IReadOnlyDictionary<string, string[]> errors) =>
        new(default, MemberServiceFailure.Validation, errors, null);

    public static MemberServiceResult<T> Conflict(string detail) =>
        new(default, MemberServiceFailure.Conflict, EmptyErrors, detail);

    private static readonly IReadOnlyDictionary<string, string[]> EmptyErrors =
        new Dictionary<string, string[]>(StringComparer.Ordinal);
}
