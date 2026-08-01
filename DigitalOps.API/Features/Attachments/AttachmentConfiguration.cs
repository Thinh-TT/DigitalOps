using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DigitalOps.API.Features.Attachments;

public sealed class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.ToTable("attachments", table =>
        {
            table.HasCheckConstraint(
                "ck_attachments_extraction_status",
                "extraction_status IN ('Pending', 'Processing', 'Succeeded', 'Failed', 'Unsupported')");
            table.HasCheckConstraint(
                "ck_attachments_succeeded_extracted_at",
                "extraction_status <> 'Succeeded' OR extracted_at IS NOT NULL");
            table.HasCheckConstraint(
                "ck_attachments_failed_error",
                "extraction_status <> 'Failed' OR (extraction_error IS NOT NULL AND length(trim(extraction_error)) > 0)");
            table.HasCheckConstraint(
                "ck_attachments_exactly_one_parent",
                "num_nonnulls(incoming_document_id, outgoing_document_id) = 1");
        });

        builder.HasKey(attachment => attachment.Id);

        builder.Property(attachment => attachment.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("gen_random_uuid()");
        builder.Property(attachment => attachment.IncomingDocumentId)
            .HasColumnName("incoming_document_id")
            .HasColumnType("uuid");
        builder.Property(attachment => attachment.OutgoingDocumentId)
            .HasColumnName("outgoing_document_id")
            .HasColumnType("uuid");
        builder.Property(attachment => attachment.StorageKey)
            .HasColumnName("file_url")
            .HasMaxLength(2048)
            .IsRequired();
        builder.Property(attachment => attachment.FileName)
            .HasColumnName("file_name")
            .HasMaxLength(255)
            .IsRequired();
        builder.Property(attachment => attachment.UploadedByStaffId)
            .HasColumnName("uploaded_by_staff_id")
            .HasColumnType("uuid")
            .IsRequired();
        builder.Property(attachment => attachment.ExtractionStatus)
            .HasColumnName("extraction_status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(ExtractionStatus.Pending)
            .IsRequired();
        builder.Property(attachment => attachment.ExtractedText)
            .HasColumnName("extracted_text");
        builder.Property(attachment => attachment.ExtractionError)
            .HasColumnName("extraction_error");
        builder.Property(attachment => attachment.ExtractedAt)
            .HasColumnName("extracted_at")
            .HasColumnType("timestamptz");
        builder.Property(attachment => attachment.UploadedAt)
            .HasColumnName("uploaded_at")
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();
        builder.Property(attachment => attachment.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        builder.HasOne(attachment => attachment.IncomingDocument)
            .WithMany(document => document.Attachments)
            .HasForeignKey(attachment => attachment.IncomingDocumentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(attachment => attachment.OutgoingDocument)
            .WithMany(document => document.Attachments)
            .HasForeignKey(attachment => attachment.OutgoingDocumentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(attachment => attachment.UploadedByStaff)
            .WithMany()
            .HasForeignKey(attachment => attachment.UploadedByStaffId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasIndex(attachment => attachment.IncomingDocumentId)
            .HasDatabaseName("ix_attachments_incoming_document_id");
        builder.HasIndex(attachment => attachment.OutgoingDocumentId)
            .HasDatabaseName("ix_attachments_outgoing_document_id");
        builder.HasIndex(attachment => attachment.UploadedByStaffId)
            .HasDatabaseName("ix_attachments_uploaded_by_staff_id");
        builder.HasIndex(attachment => attachment.ExtractionStatus)
            .HasDatabaseName("ix_attachments_extraction_status");
    }
}
