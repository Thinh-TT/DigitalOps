using DigitalOps.API.Features.Drafting;
using DigitalOps.API.Features.Members;
using DigitalOps.API.Shared.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DigitalOps.API.Shared.Data;

public sealed class DigitalOpsDbContext(
    DbContextOptions<DigitalOpsDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Member> Members => Set<Member>();

    public DbSet<Staff> Staff => Set<Staff>();

    public DbSet<DocumentType> DocumentTypes => Set<DocumentType>();

    public DbSet<DocumentTemplate> DocumentTemplates => Set<DocumentTemplate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ApplicationUser>().ToTable("asp_net_users");
        modelBuilder.Entity<IdentityRole<Guid>>().ToTable("asp_net_roles");
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("asp_net_user_roles");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("asp_net_user_claims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("asp_net_user_logins");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("asp_net_role_claims");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("asp_net_user_tokens");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DigitalOpsDbContext).Assembly);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyAuditTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ApplyAuditTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ApplyAuditTimestamps()
    {
        var utcNow = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<IAuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = utcNow;
                entry.Entity.UpdatedAt = utcNow;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = utcNow;
            }
        }
    }
}
