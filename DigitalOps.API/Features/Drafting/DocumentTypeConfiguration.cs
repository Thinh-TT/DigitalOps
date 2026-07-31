using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DigitalOps.API.Features.Drafting;

public sealed class DocumentTypeConfiguration : IEntityTypeConfiguration<DocumentType>
{
    public void Configure(EntityTypeBuilder<DocumentType> builder)
    {
        builder.ToTable("document_types");

        builder.HasKey(documentType => documentType.Id);

        builder.Property(documentType => documentType.Id)
            .HasColumnType("uuid")
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("gen_random_uuid()");
        builder.Property(documentType => documentType.Code).HasMaxLength(50).IsRequired();
        builder.Property(documentType => documentType.Name).HasMaxLength(150).IsRequired();
        builder.Property(documentType => documentType.IsActive).HasDefaultValue(true).IsRequired();
        builder.Property(documentType => documentType.CreatedAt)
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();
        builder.Property(documentType => documentType.UpdatedAt)
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        builder.HasIndex(documentType => documentType.Code)
            .IsUnique()
            .HasDatabaseName("ux_document_types_code");
        builder.HasIndex(documentType => documentType.IsActive)
            .HasDatabaseName("ix_document_types_is_active");
    }
}
