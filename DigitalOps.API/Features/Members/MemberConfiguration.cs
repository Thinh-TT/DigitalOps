using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DigitalOps.API.Features.Members;

public sealed class MemberConfiguration : IEntityTypeConfiguration<Member>
{
    public void Configure(EntityTypeBuilder<Member> builder)
    {
        builder.ToTable("members", table =>
            table.HasCheckConstraint("ck_members_status", "status IN ('Active', 'Inactive')"));

        builder.HasKey(member => member.Id);

        builder.Property(member => member.Id)
            .HasColumnType("uuid")
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("gen_random_uuid()");
        builder.Property(member => member.FullName).HasMaxLength(200).IsRequired();
        builder.Property(member => member.DateOfBirth).HasColumnType("date");
        builder.Property(member => member.Gender).HasMaxLength(20);
        builder.Property(member => member.Phone).HasMaxLength(30);
        builder.Property(member => member.Email).HasMaxLength(254);
        builder.Property(member => member.Position).HasMaxLength(150);
        builder.Property(member => member.JoinDate).HasColumnType("date");
        builder.Property(member => member.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(MemberStatus.Active)
            .IsRequired();
        builder.Property(member => member.CreatedAt)
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();
        builder.Property(member => member.UpdatedAt)
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        builder.HasIndex(member => member.FullName).HasDatabaseName("ix_members_full_name");
        builder.HasIndex(member => member.Status).HasDatabaseName("ix_members_status");
        builder.HasIndex(member => member.Phone).HasDatabaseName("ix_members_phone");
        builder.HasIndex(member => member.Email).HasDatabaseName("ix_members_email");
    }
}
