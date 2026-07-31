using System.ComponentModel.DataAnnotations;
using DigitalOps.API.Shared.Api;
using DigitalOps.API.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace DigitalOps.API.Features.Members;

public sealed class MemberManagementService(
    DigitalOpsDbContext dbContext) : IMemberManagementService
{
    private static readonly IReadOnlySet<string> AllowedGenders =
        new HashSet<string>(
            ["Male", "Female", "Other"],
            StringComparer.Ordinal);

    public async Task<PagedResponse<MemberResponse>> GetListAsync(
        MemberListQuery query,
        CancellationToken cancellationToken = default)
    {
        var members = dbContext.Members.AsNoTracking();
        var normalizedQuery = NormalizeSearchQuery(query.Q);

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
        var normalizedQuery = NormalizeSearchQuery(query.Q);

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
            FullName = NormalizeFullName(request.FullName!),
            DateOfBirth = request.DateOfBirth,
            Gender = NormalizeOptional(request.Gender),
            Address = NormalizeOptional(request.Address),
            Phone = NormalizePhone(request.Phone),
            Email = NormalizeEmail(request.Email),
            Position = NormalizeOptional(request.Position),
            JoinDate = request.JoinDate,
            Status = MemberStatus.Active,
            Notes = NormalizeOptional(request.Notes)
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
            member.FullName = NormalizeFullName(request.FullName!);
        }

        if (request.HasDateOfBirth)
        {
            member.DateOfBirth = request.DateOfBirth;
        }

        if (request.HasGender)
        {
            member.Gender = NormalizeOptional(request.Gender);
        }

        if (request.HasAddress)
        {
            member.Address = NormalizeOptional(request.Address);
        }

        if (request.HasPhone)
        {
            member.Phone = NormalizePhone(request.Phone);
        }

        if (request.HasEmail)
        {
            member.Email = NormalizeEmail(request.Email);
        }

        if (request.HasPosition)
        {
            member.Position = NormalizeOptional(request.Position);
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
            member.Notes = NormalizeOptional(request.Notes);
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
            AddError(errors, "fullName", "Họ và tên không được để trống.");
        }

        if (request.HasFullName && request.FullName?.Length > 200)
        {
            AddError(errors, "fullName", "Họ và tên không được vượt quá 200 ký tự.");
        }

        var gender = NormalizeOptional(request.Gender);
        if (request.HasGender
            && gender is not null
            && !AllowedGenders.Contains(gender))
        {
            AddError(
                errors,
                "gender",
                "Giới tính phải là Male, Female hoặc Other.");
        }

        var phone = NormalizePhone(request.Phone);
        if (request.HasPhone
            && phone is not null
            && !new PhoneAttribute().IsValid(phone))
        {
            AddError(errors, "phone", "Số điện thoại không đúng định dạng.");
        }

        var email = NormalizeEmail(request.Email);
        if (request.HasEmail
            && email is not null
            && !new EmailAddressAttribute().IsValid(email))
        {
            AddError(errors, "email", "Email không đúng định dạng.");
        }

        if (request.HasStatus && request.Status != MemberStatus.Active)
        {
            AddError(
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

    private static void AddError(
        IDictionary<string, List<string>> errors,
        string field,
        string message)
    {
        if (!errors.TryGetValue(field, out var fieldErrors))
        {
            fieldErrors = [];
            errors[field] = fieldErrors;
        }

        fieldErrors.Add(message);
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

    private static string NormalizeFullName(string value) =>
        CollapseWhitespace(value);

    private static string? NormalizePhone(string? value)
    {
        var normalized = NormalizeOptional(value);
        return normalized is null ? null : CollapseWhitespace(normalized);
    }

    private static string? NormalizeEmail(string? value) =>
        NormalizeOptional(value)?.ToLowerInvariant();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeSearchQuery(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : CollapseWhitespace(value).ToLowerInvariant();

    private static string CollapseWhitespace(string value) =>
        string.Join(
            ' ',
            value.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries));
}
