using DigitalOps.API.Features.Drafting;
using DigitalOps.API.Features.Members;
using DigitalOps.API.Features.IncomingDocuments;
using DigitalOps.API.Shared.Data;
using DigitalOps.API.Shared.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DigitalOps.API.Tests;

public sealed class BaselineModelTests
{
    [Fact]
    public void Baseline_model_uses_the_required_tables_columns_constraints_and_indexes()
    {
        using var dbContext = CreateDbContext();
        var model = dbContext.GetService<IDesignTimeModel>().Model;

        var member = GetEntityType<Member>(model);
        var staff = GetEntityType<Staff>(model);
        var documentType = GetEntityType<DocumentType>(model);
        var documentTemplate = GetEntityType<DocumentTemplate>(model);
        var incomingDocument = GetEntityType<IncomingDocument>(model);

        Assert.Equal("members", member.GetTableName());
        Assert.Equal("uuid", member.FindProperty(nameof(Member.Id))!.GetColumnType());
        Assert.Equal("date", member.FindProperty(nameof(Member.DateOfBirth))!.GetColumnType());
        Assert.Equal("timestamptz", member.FindProperty(nameof(Member.CreatedAt))!.GetColumnType());
        Assert.Equal("gen_random_uuid()", member.FindProperty(nameof(Member.Id))!.GetDefaultValueSql());
        Assert.Equal("CURRENT_TIMESTAMP", member.FindProperty(nameof(Member.UpdatedAt))!.GetDefaultValueSql());
        Assert.Equal("status IN ('Active', 'Inactive')", GetCheckConstraint(member, "ck_members_status").Sql);
        AssertIndex(member, "ix_members_full_name", isUnique: false);
        AssertIndex(member, "ix_members_status", isUnique: false);
        AssertIndex(member, "ix_members_phone", isUnique: false);
        AssertIndex(member, "ix_members_email", isUnique: false);

        Assert.Equal("staff", staff.GetTableName());
        AssertIndex(staff, "ux_staff_identity_user_id", isUnique: true);
        Assert.Equal(
            DeleteBehavior.Restrict,
            staff.GetForeignKeys().Single(foreignKey => foreignKey.Properties.Single().Name == nameof(Staff.IdentityUserId)).DeleteBehavior);

        Assert.Equal("document_types", documentType.GetTableName());
        AssertIndex(documentType, "ux_document_types_code", isUnique: true);
        AssertIndex(documentType, "ix_document_types_is_active", isUnique: false);

        Assert.Equal("document_templates", documentTemplate.GetTableName());
        Assert.Equal("jsonb", documentTemplate.FindProperty(nameof(DocumentTemplate.FormatRules))!.GetColumnType());
        Assert.Equal("'{}'::jsonb", documentTemplate.FindProperty(nameof(DocumentTemplate.FormatRules))!.GetDefaultValueSql());
        Assert.Equal(
            "jsonb_typeof(format_rules) = 'object'",
            GetCheckConstraint(documentTemplate, "ck_document_templates_format_rules_object").Sql);
        AssertIndex(documentTemplate, "ix_document_templates_document_type_id", isUnique: false);
        AssertIndex(documentTemplate, "ix_document_templates_is_active", isUnique: false);
        AssertIndex(documentTemplate, "ux_document_templates_type_name", isUnique: true);

        Assert.Equal("incoming_documents", incomingDocument.GetTableName());
        Assert.Equal("date", incomingDocument.FindProperty(nameof(IncomingDocument.ReceivedDate))!.GetColumnType());
        Assert.Equal("date", incomingDocument.FindProperty(nameof(IncomingDocument.Deadline))!.GetColumnType());
        Assert.Equal("numeric(5,4)", incomingDocument.FindProperty(nameof(IncomingDocument.AssignmentConfidence))!.GetColumnType());
        Assert.Equal("timestamptz", incomingDocument.FindProperty(nameof(IncomingDocument.CompletedAt))!.GetColumnType());
        Assert.Equal(
            "status IN ('New', 'InProgress', 'Completed', 'Overdue')",
            GetCheckConstraint(incomingDocument, "ck_incoming_documents_status").Sql);
        Assert.Equal(
            "received_date <= deadline",
            GetCheckConstraint(incomingDocument, "ck_incoming_documents_received_deadline").Sql);
        Assert.Equal(
            6,
            incomingDocument.GetCheckConstraints().Count());
        AssertIndex(incomingDocument, "ix_incoming_documents_document_type_id", isUnique: false);
        AssertIndex(incomingDocument, "ix_incoming_documents_status_deadline", isUnique: false);
        AssertIndex(incomingDocument, "ix_incoming_documents_assigned_status", isUnique: false);
        AssertIndex(incomingDocument, "ix_incoming_documents_reference_sender", isUnique: false);
        AssertIndex(incomingDocument, "ix_incoming_documents_suggested_staff_id", isUnique: false);
        AssertIndex(incomingDocument, "ix_incoming_documents_confirmed_by_staff_id", isUnique: false);
        Assert.All(
            incomingDocument.GetForeignKeys(),
            foreignKey => Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior));

        Assert.Equal("asp_net_users", GetEntityType<ApplicationUser>(model).GetTableName());
        Assert.Equal("asp_net_roles", model.FindEntityType("Microsoft.AspNetCore.Identity.IdentityRole<System.Guid>")!.GetTableName());
    }

    private static DigitalOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<DigitalOpsDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=digitalops_test;Username=test;Password=test")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new DigitalOpsDbContext(options);
    }

    private static IEntityType GetEntityType<TEntity>(IModel model)
        where TEntity : class =>
        model.FindEntityType(typeof(TEntity))
        ?? throw new InvalidOperationException($"Entity type {typeof(TEntity).Name} was not found.");

    private static ICheckConstraint GetCheckConstraint(IEntityType entityType, string name) =>
        entityType.GetCheckConstraints().Single(constraint => constraint.Name == name);

    private static void AssertIndex(IEntityType entityType, string databaseName, bool isUnique)
    {
        var index = entityType.GetIndexes().Single(index => index.GetDatabaseName() == databaseName);

        Assert.Equal(isUnique, index.IsUnique);
    }
}
