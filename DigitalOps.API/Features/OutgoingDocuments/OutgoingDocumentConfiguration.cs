using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DigitalOps.API.Features.OutgoingDocuments;

public sealed class OutgoingDocumentConfiguration
    : IEntityTypeConfiguration<OutgoingDocument>
{
    public void Configure(EntityTypeBuilder<OutgoingDocument> builder)
    {
        builder.ToTable("outgoing_documents", table =>
        {
            table.HasCheckConstraint(
                "ck_outgoing_documents_status",
                "status IN ('AiDraft', 'Editing', 'PendingReview', 'ReviewFailed', 'PendingApproval', 'Approved', 'Archived')");
            table.HasCheckConstraint(
                "ck_outgoing_documents_review_issues_array",
                "jsonb_typeof(review_issues) = 'array'");
            table.HasCheckConstraint(
                "ck_outgoing_documents_approved_tuple",
                "(approved_by_staff_id IS NULL AND approved_at IS NULL) OR (approved_by_staff_id IS NOT NULL AND approved_at IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_outgoing_documents_reference_tuple",
                "(reference_number IS NULL AND issued_date IS NULL) OR (reference_number IS NOT NULL AND issued_date IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_outgoing_documents_ai_draft_content",
                "status <> 'AiDraft' OR ai_draft_content IS NOT NULL");
            table.HasCheckConstraint(
                "ck_outgoing_documents_approved_status",
                "status NOT IN ('Approved', 'Archived') OR (approved_by_staff_id IS NOT NULL AND approved_at IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_outgoing_documents_archived_tuple",
                "(status = 'Archived' AND archived_at IS NOT NULL AND reference_number IS NOT NULL AND issued_date IS NOT NULL) OR (status <> 'Archived' AND archived_at IS NULL)");
        });

        builder.HasKey(document => document.Id);

        builder.Property(document => document.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("gen_random_uuid()");
        builder.Property(document => document.TemplateId).HasColumnName("template_id").HasColumnType("uuid").IsRequired();
        builder.Property(document => document.RelatedIncomingDocumentId).HasColumnName("related_incoming_document_id").HasColumnType("uuid");
        builder.Property(document => document.RelatedMemberId).HasColumnName("related_member_id").HasColumnType("uuid");
        builder.Property(document => document.Title).HasColumnName("title").HasMaxLength(500).IsRequired();
        builder.Property(document => document.Content).HasColumnName("content").IsRequired();
        builder.Property(document => document.AiDraftContent).HasColumnName("ai_draft_content");
        builder.Property(document => document.DraftedByStaffId).HasColumnName("drafted_by_staff_id").HasColumnType("uuid").IsRequired();
        builder.Property(document => document.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(OutgoingDocumentStatus.Editing)
            .IsRequired();
        builder.Property(document => document.ReviewIssues)
            .HasColumnName("review_issues")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb")
            .IsRequired();
        builder.Property(document => document.ApprovedByStaffId).HasColumnName("approved_by_staff_id").HasColumnType("uuid");
        builder.Property(document => document.ApprovedAt).HasColumnName("approved_at").HasColumnType("timestamptz");
        builder.Property(document => document.ReferenceNumber).HasColumnName("reference_number").HasMaxLength(100);
        builder.Property(document => document.IssuedDate).HasColumnName("issued_date").HasColumnType("date");
        builder.Property(document => document.ArchivedAt).HasColumnName("archived_at").HasColumnType("timestamptz");
        builder.Property(document => document.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();
        builder.Property(document => document.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        builder.HasOne(document => document.Template)
            .WithMany()
            .HasForeignKey(document => document.TemplateId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
        builder.HasOne(document => document.RelatedIncomingDocument)
            .WithMany()
            .HasForeignKey(document => document.RelatedIncomingDocumentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(document => document.RelatedMember)
            .WithMany()
            .HasForeignKey(document => document.RelatedMemberId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(document => document.DraftedByStaff)
            .WithMany()
            .HasForeignKey(document => document.DraftedByStaffId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
        builder.HasOne(document => document.ApprovedByStaff)
            .WithMany()
            .HasForeignKey(document => document.ApprovedByStaffId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(document => document.Status).HasDatabaseName("ix_outgoing_documents_status");
        builder.HasIndex(document => document.TemplateId).HasDatabaseName("ix_outgoing_documents_template_id");
        builder.HasIndex(document => document.RelatedIncomingDocumentId).HasDatabaseName("ix_outgoing_documents_related_incoming_document_id");
        builder.HasIndex(document => document.RelatedMemberId).HasDatabaseName("ix_outgoing_documents_related_member_id");
        builder.HasIndex(document => document.DraftedByStaffId).HasDatabaseName("ix_outgoing_documents_drafted_by_staff_id");
        builder.HasIndex(document => document.ApprovedByStaffId).HasDatabaseName("ix_outgoing_documents_approved_by_staff_id");
        builder.HasIndex(document => document.ReferenceNumber)
            .IsUnique()
            .HasDatabaseName("ux_outgoing_documents_reference_number")
            .HasFilter("reference_number IS NOT NULL");
        builder.HasIndex(document => document.CreatedAt).HasDatabaseName("ix_outgoing_documents_created_at");
    }
}
