using DigitalOps.API.Features.Attachments;
using DigitalOps.API.Features.Drafting;
using DigitalOps.API.Features.IncomingDocuments;
using DigitalOps.API.Features.Members;
using DigitalOps.API.Features.OutgoingDocuments;
using DigitalOps.API.Features.Reminders;
using DigitalOps.API.Features.Review;
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

    public DbSet<IncomingDocument> IncomingDocuments => Set<IncomingDocument>();

    public DbSet<OutgoingDocument> OutgoingDocuments => Set<OutgoingDocument>();

    public DbSet<Attachment> Attachments => Set<Attachment>();

    public DbSet<ReminderHistory> ReminderHistory => Set<ReminderHistory>();

    public DbSet<ReviewHistory> ReviewHistory => Set<ReviewHistory>();

    public DbSet<DigitalOps.API.Shared.Data.Entities.RagDocument> RagDocuments => Set<DigitalOps.API.Shared.Data.Entities.RagDocument>();

    public DbSet<DigitalOps.API.Shared.Data.Entities.RagDocumentVersion> RagDocumentVersions => Set<DigitalOps.API.Shared.Data.Entities.RagDocumentVersion>();

    public DbSet<DigitalOps.API.Shared.Data.Entities.RagDocumentSource> RagDocumentSources => Set<DigitalOps.API.Shared.Data.Entities.RagDocumentSource>();

    public DbSet<DigitalOps.API.Shared.Data.Entities.RagChunkSet> RagChunkSets => Set<DigitalOps.API.Shared.Data.Entities.RagChunkSet>();

    public DbSet<DigitalOps.API.Shared.Data.Entities.RagChunk> RagChunks => Set<DigitalOps.API.Shared.Data.Entities.RagChunk>();

    public DbSet<DigitalOps.API.Shared.Data.Entities.RagIndexGeneration> RagIndexGenerations => Set<DigitalOps.API.Shared.Data.Entities.RagIndexGeneration>();

    public DbSet<DigitalOps.API.Shared.Data.Entities.RagIndexPoint> RagIndexPoints => Set<DigitalOps.API.Shared.Data.Entities.RagIndexPoint>();

    public DbSet<DigitalOps.API.Shared.Data.Entities.RagCitationSnapshot> RagCitationSnapshots => Set<DigitalOps.API.Shared.Data.Entities.RagCitationSnapshot>();

    public DbSet<DigitalOps.API.Shared.Data.Entities.RagIngestionJob> RagIngestionJobs => Set<DigitalOps.API.Shared.Data.Entities.RagIngestionJob>();

    public DbSet<DigitalOps.API.Shared.Data.Entities.RagIngestionError> RagIngestionErrors => Set<DigitalOps.API.Shared.Data.Entities.RagIngestionError>();

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
