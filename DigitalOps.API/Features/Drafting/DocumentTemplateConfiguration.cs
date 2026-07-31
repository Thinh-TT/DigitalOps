using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DigitalOps.API.Features.Drafting;

public sealed class DocumentTemplateConfiguration : IEntityTypeConfiguration<DocumentTemplate>
{
    public void Configure(EntityTypeBuilder<DocumentTemplate> builder)
    {
        builder.ToTable("document_templates", table =>
            table.HasCheckConstraint(
                "ck_document_templates_format_rules_object",
                "jsonb_typeof(format_rules) = 'object'"));

        builder.HasKey(template => template.Id);

        builder.Property(template => template.Id)
            .HasColumnType("uuid")
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("gen_random_uuid()");
        builder.Property(template => template.DocumentTypeId).HasColumnType("uuid").IsRequired();
        builder.Property(template => template.Name).HasMaxLength(200).IsRequired();
        builder.Property(template => template.TemplateContent).IsRequired();
        builder.Property(template => template.FormatRules)
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb")
            .IsRequired();
        builder.Property(template => template.IsActive).HasDefaultValue(true).IsRequired();
        builder.Property(template => template.CreatedAt)
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();
        builder.Property(template => template.UpdatedAt)
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        builder.HasOne(template => template.DocumentType)
            .WithMany(documentType => documentType.Templates)
            .HasForeignKey(template => template.DocumentTypeId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasIndex(template => template.DocumentTypeId)
            .HasDatabaseName("ix_document_templates_document_type_id");
        builder.HasIndex(template => template.IsActive)
            .HasDatabaseName("ix_document_templates_is_active");
        builder.HasIndex(template => new { template.DocumentTypeId, template.Name })
            .IsUnique()
            .HasDatabaseName("ux_document_templates_type_name");
    }
}
