using System.Data;
using DigitalOps.API.Shared.Api;
using DigitalOps.API.Shared.Data;
using DigitalOps.API.Shared.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DigitalOps.API.Features.StaffManagement;

public sealed class StaffManagementService(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    DigitalOpsDbContext dbContext) : IStaffManagementService
{
    private const string RolesField = "roles";
    private const string TemporaryPasswordField = "temporaryPassword";

    public async Task<PagedResponse<StaffResponse>> GetListAsync(
        StaffListQuery query,
        CancellationToken cancellationToken = default)
    {
        var staffQuery = dbContext.Staff
            .AsNoTracking()
            .Include(staff => staff.IdentityUser)
            .Where(staff => query.ActiveOnly != true || staff.IsActive);

        var totalCount = await staffQuery.CountAsync(cancellationToken);
        var staffItems = await staffQuery
            .OrderBy(staff => staff.FullName)
            .ThenBy(staff => staff.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);
        var roleMap = await GetRoleMapAsync(
            staffItems.Select(staff => staff.IdentityUserId).ToArray(),
            cancellationToken);

        var items = staffItems
            .Select(staff => ToResponse(
                staff,
                roleMap.GetValueOrDefault(staff.IdentityUserId, [])))
            .ToArray();

        return new PagedResponse<StaffResponse>(
            items,
            query.Page,
            query.PageSize,
            totalCount,
            totalCount == 0
                ? 0
                : (int)Math.Ceiling((double)totalCount / query.PageSize));
    }

    public async Task<StaffResponse?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var staff = await FindStaffAsync(id, tracking: false, cancellationToken);
        return staff is null
            ? null
            : ToResponse(
                staff,
                await GetRolesAsync(staff.IdentityUserId, cancellationToken));
    }

    public async Task<StaffServiceResult<StaffResponse>> CreateAsync(
        StaffCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var requestErrors = ValidateCreateRequest(request);
        if (requestErrors.Count > 0)
        {
            return StaffServiceResult<StaffResponse>.Validation(requestErrors);
        }

        var rolesResult = await NormalizeAndValidateRolesAsync(
            request.Roles,
            cancellationToken);
        if (rolesResult.Errors is not null)
        {
            return StaffServiceResult<StaffResponse>.Validation(
                RolesField,
                rolesResult.Errors);
        }

        var userName = request.UserName.Trim();
        var email = request.Email.Trim();
        var fullName = request.FullName.Trim();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            Email = email,
            EmailConfirmed = true,
            LockoutEnabled = true,
            MustChangePassword = true
        };

        var createUserResult = await userManager.CreateAsync(
            user,
            request.TemporaryPassword);
        if (!createUserResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return MapIdentityErrors(createUserResult.Errors);
        }

        var addRolesResult = await userManager.AddToRolesAsync(
            user,
            rolesResult.Roles);
        if (!addRolesResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InvalidOperationException(
                "The configured Identity roles could not be assigned.");
        }

        var staff = new Staff
        {
            Id = Guid.NewGuid(),
            IdentityUserId = user.Id,
            IdentityUser = user,
            FullName = fullName,
            Position = NormalizeOptional(request.Position),
            Department = NormalizeOptional(request.Department),
            Email = email,
            Phone = NormalizeOptional(request.Phone),
            IsActive = true
        };

        dbContext.Staff.Add(staff);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return StaffServiceResult<StaffResponse>.Success(
            ToResponse(staff, rolesResult.Roles));
    }

    public async Task<StaffServiceResult<StaffResponse>> UpdateAsync(
        Guid id,
        StaffUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        var requestErrors = ValidateUpdateRequest(request);
        if (requestErrors.Count > 0)
        {
            return StaffServiceResult<StaffResponse>.Validation(requestErrors);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var staff = await FindStaffAsync(id, tracking: true, cancellationToken);

        if (staff is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return StaffServiceResult<StaffResponse>.NotFound();
        }

        if (request.HasIsActive
            && request.IsActive == false
            && staff.IsActive
            && await IsAdministratorAsync(staff.IdentityUserId, cancellationToken)
            && await CountActiveAdministratorsAsync(cancellationToken) <= 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return StaffServiceResult<StaffResponse>.Conflict(
                "Không thể vô hiệu hóa Administrator đang hoạt động cuối cùng.");
        }

        if (request.HasFullName)
        {
            staff.FullName = request.FullName!.Trim();
        }

        if (request.HasPosition)
        {
            staff.Position = NormalizeOptional(request.Position);
        }

        if (request.HasDepartment)
        {
            staff.Department = NormalizeOptional(request.Department);
        }

        if (request.HasPhone)
        {
            staff.Phone = NormalizeOptional(request.Phone);
        }

        if (request.HasIsActive)
        {
            staff.IsActive = request.IsActive!.Value;
        }

        if (request.HasEmail)
        {
            var email = request.Email!.Trim();
            staff.Email = email;
            staff.IdentityUser.Email = email;
            staff.IdentityUser.EmailConfirmed = true;

            var updateUserResult = await userManager.UpdateAsync(staff.IdentityUser);
            if (!updateUserResult.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                return MapIdentityErrors(updateUserResult.Errors);
            }
        }
        else
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var roles = await userManager.GetRolesAsync(staff.IdentityUser);
        await transaction.CommitAsync(cancellationToken);

        return StaffServiceResult<StaffResponse>.Success(
            ToResponse(staff, NormalizeRoles(roles)));
    }

    public async Task<StaffServiceResult<StaffResponse>> ReplaceRolesAsync(
        Guid id,
        RoleAssignmentRequest request,
        CancellationToken cancellationToken = default)
    {
        var rolesResult = await NormalizeAndValidateRolesAsync(
            request.Roles,
            cancellationToken);
        if (rolesResult.Errors is not null)
        {
            return StaffServiceResult<StaffResponse>.Validation(
                RolesField,
                rolesResult.Errors);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var staff = await FindStaffAsync(id, tracking: true, cancellationToken);

        if (staff is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return StaffServiceResult<StaffResponse>.NotFound();
        }

        var currentRoles = NormalizeRoles(
            await userManager.GetRolesAsync(staff.IdentityUser));
        var removesAdministrator =
            staff.IsActive
            && currentRoles.Contains(SystemRoles.Administrator, StringComparer.Ordinal)
            && !rolesResult.Roles.Contains(
                SystemRoles.Administrator,
                StringComparer.Ordinal);

        if (removesAdministrator
            && await CountActiveAdministratorsAsync(cancellationToken) <= 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return StaffServiceResult<StaffResponse>.Conflict(
                "Không thể bỏ role của Administrator đang hoạt động cuối cùng.");
        }

        var rolesToRemove = currentRoles
            .Except(rolesResult.Roles, StringComparer.Ordinal)
            .ToArray();
        var rolesToAdd = rolesResult.Roles
            .Except(currentRoles, StringComparer.Ordinal)
            .ToArray();

        if (rolesToRemove.Length > 0)
        {
            var removeResult = await userManager.RemoveFromRolesAsync(
                staff.IdentityUser,
                rolesToRemove);
            EnsureIdentityOperationSucceeded(removeResult);
        }

        if (rolesToAdd.Length > 0)
        {
            var addResult = await userManager.AddToRolesAsync(
                staff.IdentityUser,
                rolesToAdd);
            EnsureIdentityOperationSucceeded(addResult);
        }

        staff.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return StaffServiceResult<StaffResponse>.Success(
            ToResponse(staff, rolesResult.Roles));
    }

    public async Task<StaffServiceResult> ResetPasswordAsync(
        Guid id,
        ResetPasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var staff = await FindStaffAsync(id, tracking: true, cancellationToken);

        if (staff is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return StaffServiceResult.NotFound();
        }

        var resetToken = await userManager.GeneratePasswordResetTokenAsync(
            staff.IdentityUser);
        var resetResult = await userManager.ResetPasswordAsync(
            staff.IdentityUser,
            resetToken,
            request.TemporaryPassword);

        if (!resetResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return StaffServiceResult.Validation(
                TemporaryPasswordField,
                resetResult.Errors.Select(error => error.Description));
        }

        staff.IdentityUser.MustChangePassword = true;
        var updateResult = await userManager.UpdateAsync(staff.IdentityUser);
        EnsureIdentityOperationSucceeded(updateResult);

        await transaction.CommitAsync(cancellationToken);
        return StaffServiceResult.Success();
    }

    private async Task<(
        string[] Roles,
        string[]? Errors)> NormalizeAndValidateRolesAsync(
        IEnumerable<string>? requestedRoles,
        CancellationToken cancellationToken)
    {
        var roles = NormalizeRoles(requestedRoles ?? []);

        if (roles.Length == 0)
        {
            return (roles, ["Phải chọn ít nhất một role."]);
        }

        var invalidRoles = roles
            .Where(role => !SystemRoles.All.Contains(role))
            .ToArray();
        if (invalidRoles.Length > 0)
        {
            return (
                roles,
                [$"Role không hợp lệ: {string.Join(", ", invalidRoles)}."]);
        }

        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                throw new InvalidOperationException(
                    $"The required Identity role '{role}' has not been initialized.");
            }
        }

        return (roles, null);
    }

    private async Task<Staff?> FindStaffAsync(
        Guid id,
        bool tracking,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Staff
            .Include(staff => staff.IdentityUser)
            .Where(staff => staff.Id == id);

        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<string[]> GetRolesAsync(
        Guid identityUserId,
        CancellationToken cancellationToken)
    {
        var roleMap = await GetRoleMapAsync([identityUserId], cancellationToken);
        return roleMap.GetValueOrDefault(identityUserId, []);
    }

    private async Task<Dictionary<Guid, string[]>> GetRoleMapAsync(
        Guid[] identityUserIds,
        CancellationToken cancellationToken)
    {
        if (identityUserIds.Length == 0)
        {
            return [];
        }

        var roleRows = await (
                from userRole in dbContext.UserRoles.AsNoTracking()
                join role in dbContext.Roles.AsNoTracking()
                    on userRole.RoleId equals role.Id
                where identityUserIds.Contains(userRole.UserId)
                select new { userRole.UserId, role.Name })
            .ToListAsync(cancellationToken);

        return roleRows
            .Where(row => row.Name is not null)
            .GroupBy(row => row.UserId)
            .ToDictionary(
                group => group.Key,
                group => NormalizeRoles(group.Select(row => row.Name!)));
    }

    private Task<bool> IsAdministratorAsync(
        Guid identityUserId,
        CancellationToken cancellationToken) =>
        (
            from userRole in dbContext.UserRoles
            join role in dbContext.Roles on userRole.RoleId equals role.Id
            where userRole.UserId == identityUserId
                && role.NormalizedName == SystemRoles.Administrator.ToUpperInvariant()
            select userRole)
        .AnyAsync(cancellationToken);

    private Task<int> CountActiveAdministratorsAsync(
        CancellationToken cancellationToken) =>
        (
            from staff in dbContext.Staff
            join userRole in dbContext.UserRoles
                on staff.IdentityUserId equals userRole.UserId
            join role in dbContext.Roles on userRole.RoleId equals role.Id
            where staff.IsActive
                && role.NormalizedName == SystemRoles.Administrator.ToUpperInvariant()
            select staff.Id)
        .Distinct()
        .CountAsync(cancellationToken);

    private static IReadOnlyDictionary<string, string[]> ValidateUpdateRequest(
        StaffUpdateRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.HasFullName && string.IsNullOrWhiteSpace(request.FullName))
        {
            errors["fullName"] = ["Họ và tên không được để trống."];
        }

        if (request.HasEmail && string.IsNullOrWhiteSpace(request.Email))
        {
            errors["email"] = ["Email không được để trống."];
        }

        if (request.HasIsActive && request.IsActive is null)
        {
            errors["isActive"] = ["Trạng thái hoạt động phải là true hoặc false."];
        }

        return errors;
    }

    private static IReadOnlyDictionary<string, string[]> ValidateCreateRequest(
        StaffCreateRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(request.UserName))
        {
            errors["userName"] = ["Tên đăng nhập không được để trống."];
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            errors["email"] = ["Email không được để trống."];
        }

        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            errors["fullName"] = ["Họ và tên không được để trống."];
        }

        if (string.IsNullOrWhiteSpace(request.TemporaryPassword))
        {
            errors[TemporaryPasswordField] = ["Mật khẩu tạm không được để trống."];
        }

        return errors;
    }

    private static StaffServiceResult<StaffResponse> MapIdentityErrors(
        IEnumerable<IdentityError> identityErrors)
    {
        var errors = identityErrors.ToArray();

        if (errors.Any(error =>
                error.Code is "DuplicateUserName" or "DuplicateEmail"))
        {
            return StaffServiceResult<StaffResponse>.Conflict(
                "Tên đăng nhập hoặc email đã được sử dụng.");
        }

        var groupedErrors = errors
            .GroupBy(GetIdentityErrorField, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.Description).ToArray(),
                StringComparer.Ordinal);

        return StaffServiceResult<StaffResponse>.Validation(groupedErrors);
    }

    private static string GetIdentityErrorField(IdentityError error)
    {
        if (error.Code.Contains("UserName", StringComparison.Ordinal))
        {
            return "userName";
        }

        if (error.Code.Contains("Email", StringComparison.Ordinal))
        {
            return "email";
        }

        return TemporaryPasswordField;
    }

    private static StaffResponse ToResponse(
        Staff staff,
        string[] roles) =>
        new(
            staff.Id,
            staff.IdentityUserId,
            staff.IdentityUser.UserName ?? string.Empty,
            staff.FullName,
            staff.Position,
            staff.Department,
            staff.Email,
            staff.Phone,
            staff.IsActive,
            roles,
            staff.CreatedAt,
            staff.UpdatedAt);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string[] NormalizeRoles(IEnumerable<string> roles) =>
        roles
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => role.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static void EnsureIdentityOperationSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "The Identity operation could not be completed.");
        }
    }
}
