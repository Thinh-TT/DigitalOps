using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DigitalOps.API.Features.Reminders;

public sealed class ReminderConfiguration : IEntityTypeConfiguration<ReminderHistory>
{
    public void Configure(EntityTypeBuilder<ReminderHistory> builder)
    {
        builder.ToTable("reminder_history", table =>
        {
            table.HasCheckConstraint(
                "ck_reminder_history_kind",
                "reminder_kind IN ('BeforeDeadline', 'DueDate', 'Overdue')");
            table.HasCheckConstraint(
                "ck_reminder_history_delivery_status",
                "delivery_status IN ('Unread', 'Read')");
            table.HasCheckConstraint(
                "ck_reminder_history_read_at",
                "(delivery_status = 'Read' AND read_at IS NOT NULL) OR (delivery_status = 'Unread' AND read_at IS NULL)");
        });

        builder.HasKey(reminder => reminder.Id);

        builder.Property(reminder => reminder.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("gen_random_uuid()");
        builder.Property(reminder => reminder.IncomingDocumentId)
            .HasColumnName("incoming_document_id")
            .HasColumnType("uuid")
            .IsRequired();
        builder.Property(reminder => reminder.RecipientStaffId)
            .HasColumnName("recipient_staff_id")
            .HasColumnType("uuid")
            .IsRequired();
        builder.Property(reminder => reminder.ReminderKind)
            .HasColumnName("reminder_kind")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(reminder => reminder.ReminderDate)
            .HasColumnName("reminder_date")
            .HasColumnType("date")
            .IsRequired();
        builder.Property(reminder => reminder.DeliveryStatus)
            .HasColumnName("delivery_status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(ReminderDeliveryStatus.Unread)
            .IsRequired();
        builder.Property(reminder => reminder.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();
        builder.Property(reminder => reminder.ReadAt)
            .HasColumnName("read_at")
            .HasColumnType("timestamptz");

        builder.HasOne(reminder => reminder.IncomingDocument)
            .WithMany()
            .HasForeignKey(reminder => reminder.IncomingDocumentId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
        builder.HasOne(reminder => reminder.RecipientStaff)
            .WithMany()
            .HasForeignKey(reminder => reminder.RecipientStaffId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasIndex(
                reminder => new
                {
                    reminder.IncomingDocumentId,
                    reminder.RecipientStaffId,
                    reminder.ReminderKind,
                    reminder.ReminderDate
                })
            .IsUnique()
            .HasDatabaseName("ux_reminder_history_idempotency");
        builder.HasIndex(
                reminder => new
                {
                    reminder.RecipientStaffId,
                    reminder.DeliveryStatus,
                    reminder.CreatedAt
                })
            .HasDatabaseName("ix_reminder_history_recipient_status")
            .IsDescending(false, false, true);
        builder.HasIndex(reminder => reminder.IncomingDocumentId)
            .HasDatabaseName("ix_reminder_history_incoming_document_id");
    }
}
