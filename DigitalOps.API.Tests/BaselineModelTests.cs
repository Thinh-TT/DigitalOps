using DigitalOps.API.Features.Attachments;
using DigitalOps.API.Features.Drafting;
using DigitalOps.API.Features.Members;
using DigitalOps.API.Features.IncomingDocuments;
using DigitalOps.API.Features.Reminders;
using DigitalOps.API.Features.OutgoingDocuments;
using DigitalOps.API.Features.Review;
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
        var outgoingDocument = GetEntityType<OutgoingDocument>(model);
        var attachment = GetEntityType<Attachment>(model);
        var reminder = GetEntityType<ReminderHistory>(model);
        var review = GetEntityType<ReviewHistory>(model);

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

        Assert.Equal("attachments", attachment.GetTableName());
        Assert.Equal("uuid", attachment.FindProperty(nameof(Attachment.IncomingDocumentId))!.GetColumnType());
        Assert.True(attachment.FindProperty(nameof(Attachment.IncomingDocumentId))!.IsNullable);
        Assert.True(attachment.FindProperty(nameof(Attachment.OutgoingDocumentId))!.IsNullable);
        Assert.Equal("file_url", attachment.FindProperty(nameof(Attachment.StorageKey))!.GetColumnName());
        Assert.Equal("timestamptz", attachment.FindProperty(nameof(Attachment.UploadedAt))!.GetColumnType());
        Assert.Equal(
            "extraction_status IN ('Pending', 'Processing', 'Succeeded', 'Failed', 'Unsupported')",
            GetCheckConstraint(attachment, "ck_attachments_extraction_status").Sql);
        Assert.Equal(4, attachment.GetCheckConstraints().Count());
        AssertIndex(attachment, "ix_attachments_incoming_document_id", isUnique: false);
        AssertIndex(attachment, "ix_attachments_outgoing_document_id", isUnique: false);
        AssertIndex(attachment, "ix_attachments_uploaded_by_staff_id", isUnique: false);
        AssertIndex(attachment, "ix_attachments_extraction_status", isUnique: false);
        Assert.All(
            attachment.GetForeignKeys(),
            foreignKey => Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior));
        Assert.Equal(
            "num_nonnulls(incoming_document_id, outgoing_document_id) = 1",
            GetCheckConstraint(attachment, "ck_attachments_exactly_one_parent").Sql);

        Assert.Equal("outgoing_documents", outgoingDocument.GetTableName());
        Assert.Equal("uuid", outgoingDocument.FindProperty(nameof(OutgoingDocument.TemplateId))!.GetColumnType());
        Assert.Equal("jsonb", outgoingDocument.FindProperty(nameof(OutgoingDocument.ReviewIssues))!.GetColumnType());
        Assert.Equal(
            "status IN ('AiDraft', 'Editing', 'PendingReview', 'ReviewFailed', 'PendingApproval', 'Approved', 'Archived')",
            GetCheckConstraint(outgoingDocument, "ck_outgoing_documents_status").Sql);
        Assert.Equal(7, outgoingDocument.GetCheckConstraints().Count());
        AssertIndex(outgoingDocument, "ix_outgoing_documents_status", isUnique: false);
        AssertIndex(outgoingDocument, "ix_outgoing_documents_template_id", isUnique: false);
        AssertIndex(outgoingDocument, "ix_outgoing_documents_related_incoming_document_id", isUnique: false);
        AssertIndex(outgoingDocument, "ix_outgoing_documents_related_member_id", isUnique: false);
        AssertIndex(outgoingDocument, "ix_outgoing_documents_drafted_by_staff_id", isUnique: false);
        AssertIndex(outgoingDocument, "ix_outgoing_documents_approved_by_staff_id", isUnique: false);
        AssertIndex(outgoingDocument, "ux_outgoing_documents_reference_number", isUnique: true);
        AssertIndex(outgoingDocument, "ix_outgoing_documents_created_at", isUnique: false);
        Assert.All(
            outgoingDocument.GetForeignKeys(),
            foreignKey => Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior));

        Assert.Equal("reminder_history", reminder.GetTableName());
        Assert.Equal("date", reminder.FindProperty(nameof(ReminderHistory.ReminderDate))!.GetColumnType());
        Assert.Equal("timestamptz", reminder.FindProperty(nameof(ReminderHistory.CreatedAt))!.GetColumnType());
        Assert.Equal("timestamptz", reminder.FindProperty(nameof(ReminderHistory.ReadAt))!.GetColumnType());
        Assert.Equal(
            "reminder_kind IN ('BeforeDeadline', 'DueDate', 'Overdue')",
            GetCheckConstraint(reminder, "ck_reminder_history_kind").Sql);
        Assert.Equal(
            "delivery_status IN ('Unread', 'Read')",
            GetCheckConstraint(reminder, "ck_reminder_history_delivery_status").Sql);
        Assert.Equal(3, reminder.GetCheckConstraints().Count());
        AssertIndex(reminder, "ux_reminder_history_idempotency", isUnique: true);
        AssertIndex(reminder, "ix_reminder_history_recipient_status", isUnique: false);
        AssertIndex(reminder, "ix_reminder_history_incoming_document_id", isUnique: false);
        Assert.All(
            reminder.GetForeignKeys(),
            foreignKey => Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior));

        Assert.Equal("review_history", review.GetTableName());
        Assert.Equal("uuid", review.FindProperty(nameof(ReviewHistory.OutgoingDocumentId))!.GetColumnType());
        Assert.Equal("uuid", review.FindProperty(nameof(ReviewHistory.ReviewedByStaffId))!.GetColumnType());
        Assert.True(review.FindProperty(nameof(ReviewHistory.ReviewedByStaffId))!.IsNullable);
        Assert.Equal("jsonb", review.FindProperty(nameof(ReviewHistory.ReviewIssues))!.GetColumnType());
        Assert.Equal("timestamptz", review.FindProperty(nameof(ReviewHistory.ReviewedAt))!.GetColumnType());
        Assert.Equal("attempt_no > 0", GetCheckConstraint(review, "ck_review_history_attempt_no").Sql);
        Assert.Equal(
            "review_source IN ('Rule', 'AI', 'Hybrid')",
            GetCheckConstraint(review, "ck_review_history_source").Sql);
        Assert.Equal(
            "review_result IN ('Failed', 'Passed')",
            GetCheckConstraint(review, "ck_review_history_result").Sql);
        Assert.Equal(
            "jsonb_typeof(review_issues) = 'array'",
            GetCheckConstraint(review, "ck_review_history_issues_array").Sql);
        Assert.Equal(4, review.GetCheckConstraints().Count());
        AssertIndex(review, "ux_review_history_document_attempt", isUnique: true);
        AssertIndex(review, "ix_review_history_document_reviewed_at", isUnique: false);
        AssertIndex(review, "ix_review_history_reviewed_by_staff_id", isUnique: false);
        Assert.All(
            review.GetForeignKeys(),
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
