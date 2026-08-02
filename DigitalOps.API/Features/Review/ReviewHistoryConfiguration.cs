using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DigitalOps.API.Features.Review;

public sealed class ReviewHistoryConfiguration : IEntityTypeConfiguration<ReviewHistory>
{
    public void Configure(EntityTypeBuilder<ReviewHistory> builder)
    {
        builder.ToTable("review_history", table =>
        {
            table.HasCheckConstraint(
                "ck_review_history_attempt_no",
                "attempt_no > 0");
            table.HasCheckConstraint(
                "ck_review_history_source",
                "review_source IN ('Rule', 'AI', 'Hybrid')");
            table.HasCheckConstraint(
                "ck_review_history_result",
                "review_result IN ('Failed', 'Passed')");
            table.HasCheckConstraint(
                "ck_review_history_issues_array",
                "jsonb_typeof(review_issues) = 'array'");
        });

        builder.HasKey(review => review.Id);

        builder.Property(review => review.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("gen_random_uuid()");
        builder.Property(review => review.OutgoingDocumentId)
            .HasColumnName("outgoing_document_id")
            .HasColumnType("uuid")
            .IsRequired();
        builder.Property(review => review.AttemptNo)
            .HasColumnName("attempt_no")
            .IsRequired();
        builder.Property(review => review.ReviewSource)
            .HasColumnName("review_source")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(review => review.ReviewedByStaffId)
            .HasColumnName("reviewed_by_staff_id")
            .HasColumnType("uuid");
        builder.Property(review => review.ContentSnapshot)
            .HasColumnName("content_snapshot")
            .IsRequired();
        builder.Property(review => review.ReviewResult)
            .HasColumnName("review_result")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(review => review.ReviewIssues)
            .HasColumnName("review_issues")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb")
            .IsRequired();
        builder.Property(review => review.ReviewedAt)
            .HasColumnName("reviewed_at")
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        builder.HasOne(review => review.OutgoingDocument)
            .WithMany(document => document.ReviewHistory)
            .HasForeignKey(review => review.OutgoingDocumentId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
        builder.HasOne(review => review.ReviewedByStaff)
            .WithMany()
            .HasForeignKey(review => review.ReviewedByStaffId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(review => new { review.OutgoingDocumentId, review.AttemptNo })
            .IsUnique()
            .HasDatabaseName("ux_review_history_document_attempt");
        builder.HasIndex(review => new { review.OutgoingDocumentId, review.ReviewedAt })
            .HasDatabaseName("ix_review_history_document_reviewed_at")
            .IsDescending(false, true);
        builder.HasIndex(review => review.ReviewedByStaffId)
            .HasDatabaseName("ix_review_history_reviewed_by_staff_id");
    }
}
