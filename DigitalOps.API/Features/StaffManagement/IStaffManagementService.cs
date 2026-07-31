using DigitalOps.API.Shared.Api;

namespace DigitalOps.API.Features.StaffManagement;

public interface IStaffManagementService
{
    Task<PagedResponse<StaffResponse>> GetListAsync(
        StaffListQuery query,
        CancellationToken cancellationToken = default);

    Task<StaffResponse?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<StaffServiceResult<StaffResponse>> CreateAsync(
        StaffCreateRequest request,
        CancellationToken cancellationToken = default);

    Task<StaffServiceResult<StaffResponse>> UpdateAsync(
        Guid id,
        StaffUpdateRequest request,
        CancellationToken cancellationToken = default);

    Task<StaffServiceResult<StaffResponse>> ReplaceRolesAsync(
        Guid id,
        RoleAssignmentRequest request,
        CancellationToken cancellationToken = default);

    Task<StaffServiceResult> ResetPasswordAsync(
        Guid id,
        ResetPasswordRequest request,
        CancellationToken cancellationToken = default);
}

public enum StaffServiceFailure
{
    None,
    NotFound,
    Validation,
    Conflict
}

public record StaffServiceResult(
    StaffServiceFailure Failure,
    IReadOnlyDictionary<string, string[]> Errors,
    string? Detail)
{
    public bool Succeeded => Failure == StaffServiceFailure.None;

    public static StaffServiceResult Success() =>
        new(StaffServiceFailure.None, EmptyErrors, null);

    public static StaffServiceResult NotFound() =>
        new(StaffServiceFailure.NotFound, EmptyErrors, null);

    public static StaffServiceResult Validation(
        string field,
        IEnumerable<string> errors) =>
        new(
            StaffServiceFailure.Validation,
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [field] = errors.ToArray()
            },
            null);

    public static StaffServiceResult Validation(
        IReadOnlyDictionary<string, string[]> errors) =>
        new(StaffServiceFailure.Validation, errors, null);

    public static StaffServiceResult Conflict(string detail) =>
        new(StaffServiceFailure.Conflict, EmptyErrors, detail);

    private static readonly IReadOnlyDictionary<string, string[]> EmptyErrors =
        new Dictionary<string, string[]>(StringComparer.Ordinal);
}

public sealed record StaffServiceResult<T>(
    T? Value,
    StaffServiceFailure Failure,
    IReadOnlyDictionary<string, string[]> Errors,
    string? Detail)
    : StaffServiceResult(Failure, Errors, Detail)
{
    public static StaffServiceResult<T> Success(T value) =>
        new(value, StaffServiceFailure.None, EmptyErrors, null);

    public new static StaffServiceResult<T> NotFound() =>
        new(default, StaffServiceFailure.NotFound, EmptyErrors, null);

    public new static StaffServiceResult<T> Validation(
        string field,
        IEnumerable<string> errors) =>
        new(
            default,
            StaffServiceFailure.Validation,
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [field] = errors.ToArray()
            },
            null);

    public new static StaffServiceResult<T> Validation(
        IReadOnlyDictionary<string, string[]> errors) =>
        new(default, StaffServiceFailure.Validation, errors, null);

    public new static StaffServiceResult<T> Conflict(string detail) =>
        new(default, StaffServiceFailure.Conflict, EmptyErrors, detail);

    private static readonly IReadOnlyDictionary<string, string[]> EmptyErrors =
        new Dictionary<string, string[]>(StringComparer.Ordinal);
}
