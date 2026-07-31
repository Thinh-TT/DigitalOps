using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DigitalOps.API.Features.IncomingDocuments;

public sealed class IncomingDocumentConfiguration
    : IEntityTypeConfiguration<IncomingDocument>
{
    public void Configure(EntityTypeBuilder<IncomingDocument> builder)
    {
        builder.ToTable("incoming_documents", table =>
        {
            table.HasCheckConstraint(
                "ck_incoming_documents_status",
                "status IN ('New', 'InProgress', 'Completed', 'Overdue')");
            table.HasCheckConstraint(
                "ck_incoming_documents_received_deadline",
                "received_date <= deadline");
            table.HasCheckConstraint(
                "ck_incoming_documents_assignment_confidence",
                "assignment_confidence IS NULL OR (assignment_confidence >= 0 AND assignment_confidence <= 1)");
            table.HasCheckConstraint(
                "ck_incoming_documents_suggestion_tuple",
                "(suggested_staff_id IS NULL AND assignment_suggestion_reason IS NULL AND assignment_confidence IS NULL AND assignment_suggested_at IS NULL) OR (suggested_staff_id IS NOT NULL AND assignment_suggested_at IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_incoming_documents_assignment_tuple",
                "(assigned_to_staff_id IS NULL AND assignment_confirmed_by_staff_id IS NULL AND assignment_confirmed_at IS NULL) OR (assigned_to_staff_id IS NOT NULL AND assignment_confirmed_by_staff_id IS NOT NULL AND assignment_confirmed_at IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_incoming_documents_status_completed_at",
                "(status = 'Completed' AND completed_at IS NOT NULL) OR (status <> 'Completed' AND completed_at IS NULL)");
        });

        builder.HasKey(document => document.Id);

        builder.Property(document => document.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("gen_random_uuid()");
        builder.Property(document => document.ReferenceNumber)
            .HasColumnName("reference_number")
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(document => document.SenderOrg)
            .HasColumnName("sender_org")
            .HasMaxLength(255)
            .IsRequired();
        builder.Property(document => document.Summary)
            .HasColumnName("summary")
            .IsRequired();
        builder.Property(document => document.ReceivedDate)
            .HasColumnName("received_date")
            .HasColumnType("date")
            .IsRequired();
        builder.Property(document => document.Deadline)
            .HasColumnName("deadline")
            .HasColumnType("date")
            .IsRequired();
        builder.Property(document => document.DocumentTypeId)
            .HasColumnName("document_type_id")
            .HasColumnType("uuid")
            .IsRequired();
        builder.Property(document => document.SuggestedStaffId)
            .HasColumnName("suggested_staff_id")
            .HasColumnType("uuid");
        builder.Property(document => document.AssignmentSuggestionReason)
            .HasColumnName("assignment_suggestion_reason");
        builder.Property(document => document.AssignmentConfidence)
            .HasColumnName("assignment_confidence")
            .HasPrecision(5, 4);
        builder.Property(document => document.AssignmentSuggestedAt)
            .HasColumnName("assignment_suggested_at")
            .HasColumnType("timestamptz");
        builder.Property(document => document.AssignedToStaffId)
            .HasColumnName("assigned_to_staff_id")
            .HasColumnType("uuid");
        builder.Property(document => document.AssignmentConfirmedByStaffId)
            .HasColumnName("assignment_confirmed_by_staff_id")
            .HasColumnType("uuid");
        builder.Property(document => document.AssignmentConfirmedAt)
            .HasColumnName("assignment_confirmed_at")
            .HasColumnType("timestamptz");
        builder.Property(document => document.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(IncomingDocumentStatus.New)
            .IsRequired();
        builder.Property(document => document.CompletedAt)
            .HasColumnName("completed_at")
            .HasColumnType("timestamptz");
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

        builder.HasOne(document => document.DocumentType)
            .WithMany()
            .HasForeignKey(document => document.DocumentTypeId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
        builder.HasOne(document => document.SuggestedStaff)
            .WithMany()
            .HasForeignKey(document => document.SuggestedStaffId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(document => document.AssignedToStaff)
            .WithMany()
            .HasForeignKey(document => document.AssignedToStaffId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(document => document.AssignmentConfirmedByStaff)
            .WithMany()
            .HasForeignKey(document => document.AssignmentConfirmedByStaffId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(document => document.DocumentTypeId)
            .HasDatabaseName("ix_incoming_documents_document_type_id");
        builder.HasIndex(document => new { document.Status, document.Deadline })
            .HasDatabaseName("ix_incoming_documents_status_deadline");
        builder.HasIndex(document => new { document.AssignedToStaffId, document.Status })
            .HasDatabaseName("ix_incoming_documents_assigned_status");
        builder.HasIndex(document => new { document.ReferenceNumber, document.SenderOrg })
            .HasDatabaseName("ix_incoming_documents_reference_sender");
        builder.HasIndex(document => document.SuggestedStaffId)
            .HasDatabaseName("ix_incoming_documents_suggested_staff_id");
        builder.HasIndex(document => document.AssignmentConfirmedByStaffId)
            .HasDatabaseName("ix_incoming_documents_confirmed_by_staff_id");
    }
}
