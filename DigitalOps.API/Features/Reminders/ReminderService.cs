using DigitalOps.API.Shared.Api;
using DigitalOps.API.Shared.Data;
using DigitalOps.API.Features.IncomingDocuments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DigitalOps.API.Features.Reminders;

public sealed class ReminderService(
    DigitalOpsDbContext dbContext,
    TimeProvider timeProvider,
    IOptions<ReminderWorkerOptions> options) : IReminderService
{
    public async Task<ReminderServiceResult<PagedResponse<ReminderResponse>>> GetListAsync(
        ReminderListQuery query,
        Guid currentStaffId,
        bool currentStaffIsAdministrator,
        CancellationToken cancellationToken = default)
    {
        if (query.RecipientStaffId is not null && !currentStaffIsAdministrator)
        {
            return ReminderServiceResult<PagedResponse<ReminderResponse>>.Forbidden();
        }

        var recipientStaffId = query.RecipientStaffId ?? currentStaffId;
        var reminders = dbContext.ReminderHistory
            .AsNoTracking()
            .Where(reminder => reminder.RecipientStaffId == recipientStaffId);

        if (query.DeliveryStatus is not null)
        {
            reminders = reminders.Where(reminder =>
                reminder.DeliveryStatus == query.DeliveryStatus);
        }

        var totalCount = await reminders.CountAsync(cancellationToken);
        var items = await reminders
            .OrderByDescending(reminder => reminder.CreatedAt)
            .ThenBy(reminder => reminder.Id)
            .Include(reminder => reminder.IncomingDocument)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToArrayAsync(cancellationToken);

        return ReminderServiceResult<PagedResponse<ReminderResponse>>.Success(
            new PagedResponse<ReminderResponse>(
                items.Select(ToResponse).ToArray(),
                query.Page,
                query.PageSize,
                totalCount,
                totalCount == 0
                    ? 0
                    : (int)Math.Ceiling((double)totalCount / query.PageSize)));
    }

    public async Task<ReminderServiceResult<ReminderResponse>> MarkReadAsync(
        Guid id,
        Guid currentStaffId,
        bool currentStaffIsAdministrator,
        CancellationToken cancellationToken = default)
    {
        var reminder = await dbContext.ReminderHistory
            .Include(item => item.IncomingDocument)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (reminder is null)
        {
            return ReminderServiceResult<ReminderResponse>.NotFound();
        }

        if (!currentStaffIsAdministrator && reminder.RecipientStaffId != currentStaffId)
        {
            return ReminderServiceResult<ReminderResponse>.Forbidden();
        }

        if (reminder.DeliveryStatus == ReminderDeliveryStatus.Unread)
        {
            reminder.DeliveryStatus = ReminderDeliveryStatus.Read;
            reminder.ReadAt = timeProvider.GetUtcNow().UtcDateTime;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return ReminderServiceResult<ReminderResponse>.Success(ToResponse(reminder));
    }

    public async Task<ReminderProcessingResult> ProcessAsync(
        DateOnly businessDate,
        CancellationToken cancellationToken = default)
    {
        var openDocuments = await dbContext.IncomingDocuments
            .Where(document => document.Status != IncomingDocumentStatus.Completed)
            .Where(document =>
                document.Deadline <= businessDate
                || document.Deadline == businessDate.AddDays(options.Value.BeforeDeadlineDays))
            .ToArrayAsync(cancellationToken);

        var overdueDocuments = 0;
        var candidates = new List<ReminderCandidate>();

        foreach (var document in openDocuments)
        {
            ReminderKind? kind = null;

            if (document.Deadline < businessDate)
            {
                if (document.Status != IncomingDocumentStatus.Overdue)
                {
                    document.Status = IncomingDocumentStatus.Overdue;
                    overdueDocuments++;
                }

                kind = ReminderKind.Overdue;
            }
            else if (document.Deadline == businessDate)
            {
                kind = ReminderKind.DueDate;
            }
            else if (document.Deadline == businessDate.AddDays(options.Value.BeforeDeadlineDays))
            {
                kind = ReminderKind.BeforeDeadline;
            }

            if (kind is not null && document.AssignedToStaffId is not null)
            {
                candidates.Add(new ReminderCandidate(
                    document.Id,
                    document.AssignedToStaffId.Value,
                    kind.Value,
                    businessDate));
            }
        }

        if (candidates.Count == 0 && overdueDocuments == 0)
        {
            return new ReminderProcessingResult(0, 0, 0);
        }

        var candidateDocumentIds = candidates
            .Select(candidate => candidate.IncomingDocumentId)
            .Distinct()
            .ToArray();
        var existing = await dbContext.ReminderHistory
            .Where(reminder => candidateDocumentIds.Contains(reminder.IncomingDocumentId))
            .Where(reminder => reminder.ReminderDate == businessDate)
            .ToListAsync(cancellationToken);

        var existingKeys = existing
            .Select(reminder => new ReminderKey(
                reminder.IncomingDocumentId,
                reminder.RecipientStaffId,
                reminder.ReminderKind,
                reminder.ReminderDate))
            .ToHashSet();
        var createdReminders = 0;
        var existingReminders = 0;
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        foreach (var candidate in candidates)
        {
            var key = new ReminderKey(
                candidate.IncomingDocumentId,
                candidate.RecipientStaffId,
                candidate.ReminderKind,
                candidate.ReminderDate);
            if (!existingKeys.Add(key))
            {
                existingReminders++;
                continue;
            }

            dbContext.ReminderHistory.Add(new ReminderHistory
            {
                Id = Guid.NewGuid(),
                IncomingDocumentId = candidate.IncomingDocumentId,
                RecipientStaffId = candidate.RecipientStaffId,
                ReminderKind = candidate.ReminderKind,
                ReminderDate = candidate.ReminderDate,
                DeliveryStatus = ReminderDeliveryStatus.Unread,
                CreatedAt = utcNow
            });
            createdReminders++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new ReminderProcessingResult(
            overdueDocuments,
            createdReminders,
            existingReminders);
    }

    private static ReminderResponse ToResponse(ReminderHistory reminder) =>
        new(
            reminder.Id,
            reminder.IncomingDocumentId,
            reminder.IncomingDocument.ReferenceNumber,
            reminder.IncomingDocument.Summary,
            reminder.ReminderKind,
            reminder.ReminderDate,
            reminder.DeliveryStatus,
            reminder.CreatedAt,
            reminder.ReadAt);

    private readonly record struct ReminderCandidate(
        Guid IncomingDocumentId,
        Guid RecipientStaffId,
        ReminderKind ReminderKind,
        DateOnly ReminderDate);

    private readonly record struct ReminderKey(
        Guid IncomingDocumentId,
        Guid RecipientStaffId,
        ReminderKind ReminderKind,
        DateOnly ReminderDate);
}
