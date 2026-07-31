using System.Data;
using DigitalOps.API.Shared.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DigitalOps.API.Shared.Identity;

public sealed class IdentityInitializer(
    RoleManager<IdentityRole<Guid>> roleManager,
    UserManager<ApplicationUser> userManager,
    DigitalOpsDbContext dbContext,
    IOptions<IdentityBootstrapOptions> bootstrapOptions,
    ILogger<IdentityInitializer> logger) : IIdentityInitializer
{
    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureSystemRolesAsync();

        var options = bootstrapOptions.Value;
        if (!options.Enabled)
        {
            return;
        }

        if (await HasActiveAdministratorAsync(cancellationToken))
        {
            logger.LogInformation(
                "Identity bootstrap skipped because an active Administrator exists.");
            return;
        }

        var userName = options.UserName!.Trim();
        if (await userManager.FindByNameAsync(userName) is not null)
        {
            throw new InvalidOperationException(
                "The configured bootstrap username already exists, but no active Administrator is available.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            Email = options.Email!.Trim(),
            EmailConfirmed = true,
            LockoutEnabled = true,
            MustChangePassword = true
        };

        var createResult = await userManager.CreateAsync(
            user,
            options.TemporaryPassword!);
        EnsureSucceeded(createResult, "create the bootstrap Identity user");

        var roleResult = await userManager.AddToRoleAsync(
            user,
            SystemRoles.Administrator);
        EnsureSucceeded(roleResult, "assign the bootstrap Administrator role");

        dbContext.Staff.Add(new Staff
        {
            Id = Guid.NewGuid(),
            IdentityUserId = user.Id,
            IdentityUser = user,
            FullName = options.FullName!.Trim(),
            Position = NormalizeOptional(options.Position),
            Department = NormalizeOptional(options.Department),
            Email = options.Email.Trim(),
            Phone = NormalizeOptional(options.Phone),
            IsActive = true
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Bootstrapped the initial Administrator account for username {UserName}.",
            userName);
    }

    private async Task EnsureSystemRolesAsync()
    {
        foreach (var roleName in SystemRoles.All.Order(StringComparer.Ordinal))
        {
            if (await roleManager.RoleExistsAsync(roleName))
            {
                continue;
            }

            var result = await roleManager.CreateAsync(new IdentityRole<Guid>(roleName)
            {
                Id = Guid.NewGuid()
            });

            if (!result.Succeeded && !await roleManager.RoleExistsAsync(roleName))
            {
                EnsureSucceeded(result, $"create the required role '{roleName}'");
            }
        }
    }

    private Task<bool> HasActiveAdministratorAsync(
        CancellationToken cancellationToken) =>
        (
            from staff in dbContext.Staff.AsNoTracking()
            join userRole in dbContext.UserRoles.AsNoTracking()
                on staff.IdentityUserId equals userRole.UserId
            join role in dbContext.Roles.AsNoTracking()
                on userRole.RoleId equals role.Id
            where staff.IsActive
                && role.NormalizedName == SystemRoles.Administrator.ToUpperInvariant()
            select staff.Id)
        .AnyAsync(cancellationToken);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void EnsureSucceeded(
        IdentityResult result,
        string operation)
    {
        if (result.Succeeded)
        {
            return;
        }

        var errors = string.Join(
            "; ",
            result.Errors.Select(error => error.Description));
        throw new InvalidOperationException(
            $"Identity initialization could not {operation}: {errors}");
    }
}
