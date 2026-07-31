using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DigitalOps.API.Shared.Identity;

public sealed class StaffConfiguration : IEntityTypeConfiguration<Staff>
{
    public void Configure(EntityTypeBuilder<Staff> builder)
    {
        builder.ToTable("staff");

        builder.HasKey(staff => staff.Id);

        builder.Property(staff => staff.Id)
            .HasColumnType("uuid")
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("gen_random_uuid()");
        builder.Property(staff => staff.IdentityUserId).HasColumnType("uuid").IsRequired();
        builder.Property(staff => staff.FullName).HasMaxLength(200).IsRequired();
        builder.Property(staff => staff.Position).HasMaxLength(150);
        builder.Property(staff => staff.Department).HasMaxLength(200);
        builder.Property(staff => staff.Email).HasMaxLength(254).IsRequired();
        builder.Property(staff => staff.Phone).HasMaxLength(30);
        builder.Property(staff => staff.IsActive).HasDefaultValue(true).IsRequired();
        builder.Property(staff => staff.CreatedAt)
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();
        builder.Property(staff => staff.UpdatedAt)
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        builder.HasOne(staff => staff.IdentityUser)
            .WithOne(user => user.Staff)
            .HasForeignKey<Staff>(staff => staff.IdentityUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasIndex(staff => staff.IdentityUserId)
            .IsUnique()
            .HasDatabaseName("ux_staff_identity_user_id");
        builder.HasIndex(staff => staff.IsActive).HasDatabaseName("ix_staff_is_active");
        builder.HasIndex(staff => staff.Email).HasDatabaseName("ix_staff_email");
    }
}
