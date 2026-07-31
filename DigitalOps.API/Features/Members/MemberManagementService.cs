using DigitalOps.API.Shared.Api;
using DigitalOps.API.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace DigitalOps.API.Features.Members;

public sealed class MemberManagementService(
    DigitalOpsDbContext dbContext) : IMemberManagementService
{
    public async Task<PagedResponse<MemberResponse>> GetListAsync(
        MemberListQuery query,
        CancellationToken cancellationToken = default)
    {
        var members = dbContext.Members.AsNoTracking();
        var normalizedQuery = MemberProfileRules.NormalizeSearchQuery(query.Q);

        if (normalizedQuery is not null)
        {
            members = members.Where(member =>
                member.FullName.ToLower().Contains(normalizedQuery)
                || (member.Phone != null
                    && member.Phone.ToLower().Contains(normalizedQuery))
                || (member.Email != null
                    && member.Email.ToLower().Contains(normalizedQuery)));
        }

        if (query.Status is not null)
        {
            members = members.Where(member => member.Status == query.Status);
        }

        var totalCount = await members.CountAsync(cancellationToken);
        var memberItems = await members
            .OrderBy(member => member.FullName)
            .ThenBy(member => member.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToArrayAsync(cancellationToken);
        var items = memberItems.Select(ToResponse).ToArray();

        return CreatePage(items, query.Page, query.PageSize, totalCount);
    }

    public async Task<PagedResponse<MemberLookupResponse>> GetLookupAsync(
        MemberLookupQuery query,
        CancellationToken cancellationToken = default)
    {
        var members = dbContext.Members
            .AsNoTracking()
            .Where(member => member.Status == MemberStatus.Active);
        var normalizedQuery = MemberProfileRules.NormalizeSearchQuery(query.Q);

        if (normalizedQuery is not null)
        {
            members = members.Where(member =>
                member.FullName.ToLower().Contains(normalizedQuery)
                || (member.Position != null
                    && member.Position.ToLower().Contains(normalizedQuery)));
        }

        var totalCount = await members.CountAsync(cancellationToken);
        var items = await members
            .OrderBy(member => member.FullName)
            .ThenBy(member => member.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(member => new MemberLookupResponse(
                member.Id,
                member.FullName,
                member.Position))
            .ToArrayAsync(cancellationToken);

        return CreatePage(items, query.Page, query.PageSize, totalCount);
    }

    public async Task<MemberResponse?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var member = await dbContext.Members
            .AsNoTracking()
            .SingleOrDefaultAsync(
                member => member.Id == id,
                cancellationToken);
        return member is null ? null : ToResponse(member);
    }

    public async Task<MemberServiceResult<MemberResponse>> CreateAsync(
        MemberUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateRequest(request, creating: true);
        if (errors.Count > 0)
        {
            return MemberServiceResult<MemberResponse>.Validation(errors);
        }

        var member = new Member
        {
            Id = Guid.NewGuid(),
            FullName = MemberProfileRules.NormalizeFullName(request.FullName!),
            DateOfBirth = request.DateOfBirth,
            Gender = MemberProfileRules.NormalizeOptional(request.Gender),
            Address = MemberProfileRules.NormalizeOptional(request.Address),
            Phone = MemberProfileRules.NormalizePhone(request.Phone),
            Email = MemberProfileRules.NormalizeEmail(request.Email),
            Position = MemberProfileRules.NormalizeOptional(request.Position),
            JoinDate = request.JoinDate,
            Status = MemberStatus.Active,
            Notes = MemberProfileRules.NormalizeOptional(request.Notes)
        };

        dbContext.Members.Add(member);
        await dbContext.SaveChangesAsync(cancellationToken);

        return MemberServiceResult<MemberResponse>.Success(ToResponse(member));
    }

    public async Task<MemberServiceResult<MemberResponse>> UpdateAsync(
        Guid id,
        MemberUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateRequest(request, creating: false);
        if (errors.Count > 0)
        {
            return MemberServiceResult<MemberResponse>.Validation(errors);
        }

        var member = await dbContext.Members.SingleOrDefaultAsync(
            member => member.Id == id,
            cancellationToken);
        if (member is null)
        {
            return MemberServiceResult<MemberResponse>.NotFound();
        }

        ApplyPatch(member, request);
        await dbContext.SaveChangesAsync(cancellationToken);

        return MemberServiceResult<MemberResponse>.Success(ToResponse(member));
    }

    public async Task<MemberServiceResult<MemberResponse>> DeactivateAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var member = await dbContext.Members.SingleOrDefaultAsync(
            member => member.Id == id,
            cancellationToken);
        if (member is null)
        {
            return MemberServiceResult<MemberResponse>.NotFound();
        }

        if (member.Status == MemberStatus.Inactive)
        {
            return MemberServiceResult<MemberResponse>.Conflict(
                "Hội viên đã ngừng hoạt động.");
        }

        member.Status = MemberStatus.Inactive;
        await dbContext.SaveChangesAsync(cancellationToken);

        return MemberServiceResult<MemberResponse>.Success(ToResponse(member));
    }

    private static void ApplyPatch(Member member, MemberUpsertRequest request)
    {
        if (request.HasFullName)
        {
            member.FullName = MemberProfileRules.NormalizeFullName(request.FullName!);
        }

        if (request.HasDateOfBirth)
        {
            member.DateOfBirth = request.DateOfBirth;
        }

        if (request.HasGender)
        {
            member.Gender = MemberProfileRules.NormalizeOptional(request.Gender);
        }

        if (request.HasAddress)
        {
            member.Address = MemberProfileRules.NormalizeOptional(request.Address);
        }

        if (request.HasPhone)
        {
            member.Phone = MemberProfileRules.NormalizePhone(request.Phone);
        }

        if (request.HasEmail)
        {
            member.Email = MemberProfileRules.NormalizeEmail(request.Email);
        }

        if (request.HasPosition)
        {
            member.Position = MemberProfileRules.NormalizeOptional(request.Position);
        }

        if (request.HasJoinDate)
        {
            member.JoinDate = request.JoinDate;
        }

        if (request.HasStatus)
        {
            member.Status = MemberStatus.Active;
        }

        if (request.HasNotes)
        {
            member.Notes = MemberProfileRules.NormalizeOptional(request.Notes);
        }
    }

    private static IReadOnlyDictionary<string, string[]> ValidateRequest(
        MemberUpsertRequest request,
        bool creating)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        if ((creating && !request.HasFullName)
            || (request.HasFullName && string.IsNullOrWhiteSpace(request.FullName)))
        {
            MemberProfileRules.AddError(
                errors,
                "fullName",
                "Họ và tên không được để trống.");
        }
        else if (request.HasFullName)
        {
            MemberProfileRules.ValidateFullName(request.FullName, errors);
        }

        if (request.HasGender)
        {
            MemberProfileRules.ValidateGender(request.Gender, errors);
        }

        if (request.HasPhone)
        {
            MemberProfileRules.ValidatePhone(request.Phone, errors);
        }

        if (request.HasEmail)
        {
            MemberProfileRules.ValidateEmail(request.Email, errors);
        }

        if (request.HasPosition)
        {
            MemberProfileRules.ValidatePosition(request.Position, errors);
        }

        if (request.HasStatus && request.Status != MemberStatus.Active)
        {
            MemberProfileRules.AddError(
                errors,
                "status",
                creating
                    ? "Hội viên mới phải ở trạng thái Active."
                    : "Dùng action ngừng hoạt động để chuyển hội viên sang Inactive.");
        }

        return errors.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToArray(),
            StringComparer.Ordinal);
    }

    private static PagedResponse<T> CreatePage<T>(
        IReadOnlyList<T> items,
        int page,
        int pageSize,
        int totalCount) =>
        new(
            items,
            page,
            pageSize,
            totalCount,
            totalCount == 0
                ? 0
                : (int)Math.Ceiling((double)totalCount / pageSize));

    private static MemberResponse ToResponse(Member member) =>
        new(
            member.Id,
            member.FullName,
            member.DateOfBirth,
            member.Gender,
            member.Address,
            member.Phone,
            member.Email,
            member.Position,
            member.JoinDate,
            member.Status,
            member.Notes,
            member.CreatedAt,
            member.UpdatedAt);

}
