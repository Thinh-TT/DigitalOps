using System.Text.Json;
using DigitalOps.API.Features.Drafting;
using DigitalOps.API.Features.OutgoingDocuments;
using DigitalOps.API.Shared.AI;
using DigitalOps.API.Shared.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalOps.API.Tests;

public sealed class AiDraftLiveSmokeTests
{
    [Fact]
    [Trait("Category", "LiveAiDraft")]
    public async Task First_edit_and_rerun_preserve_the_first_ai_draft()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("DIGITALOPS_RUN_AI_DRAFT_SMOKE"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        using var factory = new LiveAiDraftApiFactory();
        _ = factory.Services;

        var suffix = Guid.NewGuid().ToString("N");
        var documentTypeId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        Guid staffId;

        try
        {
            await using (var seedScope = factory.Services.CreateAsyncScope())
            {
                var dbContext = seedScope.ServiceProvider.GetRequiredService<DigitalOpsDbContext>();
                staffId = await dbContext.Staff
                    .AsNoTracking()
                    .Where(staff => staff.IsActive)
                    .OrderBy(staff => staff.Id)
                    .Select(staff => staff.Id)
                    .FirstAsync();

                dbContext.DocumentTypes.Add(new DocumentType
                {
                    Id = documentTypeId,
                    Code = $"SMOKE-{suffix[..12]}",
                    Name = "AI smoke synthetic",
                    IsActive = true
                });
                dbContext.DocumentTemplates.Add(new DocumentTemplate
                {
                    Id = templateId,
                    DocumentTypeId = documentTypeId,
                    Name = $"AI-SMOKE-T3-02-{suffix}",
                    TemplateContent =
                        "# TỜ TRÌNH\n## Mục đích\nTrình nội dung hoạt động nội bộ.\n" +
                        "## Yêu cầu\nNêu căn cứ, nội dung đề xuất và phần kết luận ngắn gọn.",
                    FormatRules = JsonDocument.Parse("{}").RootElement.Clone(),
                    IsActive = true
                });
                dbContext.OutgoingDocuments.Add(new OutgoingDocument
                {
                    Id = documentId,
                    TemplateId = templateId,
                    DraftedByStaffId = staffId,
                    Title = "AI-SMOKE T3-02 synthetic",
                    Content = "Nội dung synthetic ban đầu, không chứa dữ liệu cá nhân.",
                    Status = OutgoingDocumentStatus.Editing
                });
                await dbContext.SaveChangesAsync();
            }

            var first = await GenerateAsync(factory, documentId, staffId);
            Assert.True(first.Succeeded, $"{first.Failure}: {first.Detail}");
            Assert.NotNull(first.Value);
            Assert.Equal(OutgoingDocumentStatus.AiDraft, first.Value.Status);
            Assert.Equal(first.Value.Content, first.Value.AiDraftContent);
            var immutableFirstDraft = first.Value.AiDraftContent;

            await using (var editScope = factory.Services.CreateAsyncScope())
            {
                var service = editScope.ServiceProvider.GetRequiredService<IOutgoingDocumentService>();
                var edited = await service.UpdateAsync(
                    documentId,
                    new OutgoingDocumentUpdateRequest
                    {
                        Title = "AI-SMOKE T3-02 synthetic đã chỉnh",
                        Content = "Nội dung synthetic đã chỉnh và lưu trước khi sinh lại."
                    },
                    staffId);

                Assert.True(edited.Succeeded, $"{edited.Failure}: {edited.Detail}");
                Assert.NotNull(edited.Value);
                Assert.Equal(OutgoingDocumentStatus.Editing, edited.Value.Status);
                Assert.Equal(immutableFirstDraft, edited.Value.AiDraftContent);
            }

            var rerun = await GenerateAsync(factory, documentId, staffId);
            Assert.True(rerun.Succeeded, $"{rerun.Failure}: {rerun.Detail}");
            Assert.NotNull(rerun.Value);
            Assert.Equal(OutgoingDocumentStatus.Editing, rerun.Value.Status);
            Assert.Equal(immutableFirstDraft, rerun.Value.AiDraftContent);
            Assert.False(string.IsNullOrWhiteSpace(rerun.Value.Content));
        }
        finally
        {
            await CleanupAsync(factory, documentId, templateId, documentTypeId);
        }
    }

    private static async Task<OutgoingDocumentResult<OutgoingDocumentResponse>> GenerateAsync(
        WebApplicationFactory<Program> factory,
        Guid documentId,
        Guid staffId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IOutgoingDocumentService>();
        return await service.GenerateAiDraftAsync(
            documentId,
            new AiDraftRequest
            {
                Instruction =
                    "Viết ngắn gọn bằng tiếng Việt và chỉ dùng dữ liệu synthetic đã cung cấp."
            },
            staffId);
    }

    private static async Task CleanupAsync(
        WebApplicationFactory<Program> factory,
        Guid documentId,
        Guid templateId,
        Guid documentTypeId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var qdrant = scope.ServiceProvider.GetRequiredService<IQdrantKnowledgeClient>();
        var templatePoints = await qdrant.GetTemplateStatesAsync();
        await qdrant.DeleteTemplatePointsAsync(
            templatePoints
                .Where(point => point.TemplateId == templateId)
                .Select(point => point.PointId)
                .ToArray());

        var dbContext = scope.ServiceProvider.GetRequiredService<DigitalOpsDbContext>();
        await dbContext.OutgoingDocuments
            .Where(document => document.Id == documentId)
            .ExecuteDeleteAsync();
        await dbContext.DocumentTemplates
            .Where(template => template.Id == templateId)
            .ExecuteDeleteAsync();
        await dbContext.DocumentTypes
            .Where(documentType => documentType.Id == documentTypeId)
            .ExecuteDeleteAsync();
    }
}

internal sealed class LiveAiDraftApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DocumentCatalogSeed:Enabled"] = "false",
                ["IdentityBootstrap:Enabled"] = "false",
                ["ReminderWorker:Enabled"] = "false"
            }));
    }
}
