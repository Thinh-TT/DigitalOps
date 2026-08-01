using DigitalOps.API.Shared.Api;

namespace DigitalOps.API.Features.Reminders;

public interface IReminderService
{
    Task<ReminderServiceResult<PagedResponse<ReminderResponse>>> GetListAsync(
        ReminderListQuery query,
        Guid currentStaffId,
        bool currentStaffIsAdministrator,
        CancellationToken cancellationToken = default);

    Task<ReminderServiceResult<ReminderResponse>> MarkReadAsync(
        Guid id,
        Guid currentStaffId,
        bool currentStaffIsAdministrator,
        CancellationToken cancellationToken = default);

    Task<ReminderProcessingResult> ProcessAsync(
        DateOnly businessDate,
        CancellationToken cancellationToken = default);
}

public enum ReminderServiceFailure
{
    None,
    NotFound,
    Forbidden
}

public sealed record ReminderServiceResult<T>(
    T? Value,
    ReminderServiceFailure Failure)
{
    public bool Succeeded => Failure == ReminderServiceFailure.None;

    public static ReminderServiceResult<T> Success(T value) =>
        new(value, ReminderServiceFailure.None);

    public static ReminderServiceResult<T> NotFound() =>
        new(default, ReminderServiceFailure.NotFound);

    public static ReminderServiceResult<T> Forbidden() =>
        new(default, ReminderServiceFailure.Forbidden);
}

public sealed record ReminderProcessingResult(
    int OverdueDocuments,
    int CreatedReminders,
    int ExistingReminders);
