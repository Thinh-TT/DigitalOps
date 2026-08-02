using System.Text.Json.Serialization;

namespace DigitalOps.API.Features.Approval;

public enum ApprovalDecision
{
    Approve,
    Return
}

public sealed class ApprovalDecisionRequest
{
    private ApprovalDecision _decision;

    public ApprovalDecision Decision
    {
        get => _decision;
        init
        {
            _decision = value;
            HasDecision = true;
        }
    }

    [JsonIgnore]
    public bool HasDecision { get; private set; }
}

public enum ApprovalOperationFailure
{
    None,
    Validation,
    NotFound,
    Conflict
}

public sealed record ApprovalOperationResult<T>(
    T? Value,
    ApprovalOperationFailure Failure,
    string? Detail,
    IReadOnlyDictionary<string, string[]> Errors)
{
    public bool Succeeded => Failure == ApprovalOperationFailure.None;

    public static ApprovalOperationResult<T> Success(T value) =>
        new(value, ApprovalOperationFailure.None, null, EmptyErrors());

    public static ApprovalOperationResult<T> Validation(
        IReadOnlyDictionary<string, string[]> errors) =>
        new(default, ApprovalOperationFailure.Validation, null, errors);

    public static ApprovalOperationResult<T> NotFound() =>
        new(default, ApprovalOperationFailure.NotFound, null, EmptyErrors());

    public static ApprovalOperationResult<T> Conflict(string detail) =>
        new(default, ApprovalOperationFailure.Conflict, detail, EmptyErrors());

    private static IReadOnlyDictionary<string, string[]> EmptyErrors() =>
        new Dictionary<string, string[]>();
}
