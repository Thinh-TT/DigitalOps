using System.Text.Json;
using DigitalOps.API.Features.Drafting;
using DigitalOps.API.Features.OutgoingDocuments;
using DigitalOps.API.Features.Review;
using DigitalOps.API.Shared.AI;
using DigitalOps.API.Shared.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DigitalOps.API.Tests;

public sealed class AiReviewLiveSmokeTests
{
    [Fact]
    [Trait("Category", "LiveAiReview")]
    public async Task Rule_failure_then_hybrid_pass_are_persisted_and_cleaned_up()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("DIGITALOPS_RUN_AI_REVIEW_SMOKE"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        using var factory = new LiveAiReviewApiFactory();
        _ = factory.Services;

        var suffix = Guid.NewGuid().ToString("N");
        var documentTypeId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        Guid staffId = default;

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
                    Code = $"RVW-{suffix[..12]}",
                    Name = "AI review smoke synthetic",
                    IsActive = true
                });
                dbContext.DocumentTemplates.Add(new DocumentTemplate
                {
                    Id = templateId,
                    DocumentTypeId = documentTypeId,
                    Name = $"AI-REVIEW-SMOKE-{suffix}",
                    TemplateContent = "Nội dung tổng hợp synthetic.",
                    FormatRules = JsonDocument.Parse(
                        "{\"version\":1,\"rules\":[{\"code\":\"national_header\",\"required\":true},{\"code\":\"reference_number\",\"required\":true},{\"code\":\"signature_block\",\"required\":true}]}")
                        .RootElement.Clone(),
                    IsActive = true
                });
                dbContext.OutgoingDocuments.Add(new OutgoingDocument
                {
                    Id = documentId,
                    TemplateId = templateId,
                    DraftedByStaffId = staffId,
                    Title = "AI-REVIEW-SMOKE synthetic",
                    Content = "Số: 01/SMOKE\nNội dung synthetic thiếu quốc hiệu.",
                    Status = OutgoingDocumentStatus.Editing
                });
                await dbContext.SaveChangesAsync();
            }

            var ruleFailure = await ReviewAsync(factory, documentId, staffId);
            Assert.True(ruleFailure.Succeeded, $"{ruleFailure.Failure}: {ruleFailure.Detail}");
            Assert.NotNull(ruleFailure.Value);
            Assert.Equal(ReviewSource.Rule, ruleFailure.Value.ReviewSource);
            Assert.Equal(ReviewResult.Failed, ruleFailure.Value.ReviewResult);
            Assert.Contains(ruleFailure.Value.ReviewIssues, issue => issue.Severity == "Error");

            await using (var editScope = factory.Services.CreateAsyncScope())
            {
                var service = editScope.ServiceProvider.GetRequiredService<IOutgoingDocumentService>();
                var edited = await service.UpdateAsync(
                    documentId,
                    new OutgoingDocumentUpdateRequest
                    {
                        Content =
                            "Số: 02/SMOKE\nCỘNG HÒA XÃ HỘI CHỦ NGHĨA VIỆT NAM\nĐộc lập - Tự do - Hạnh phúc\nNội dung synthetic để kiểm tra thể thức.\nĐẠI DIỆN CƠ QUAN\nKý, ghi rõ họ tên"
                    },
                    staffId);

                Assert.True(edited.Succeeded, $"{edited.Failure}: {edited.Detail}");
                Assert.Equal(OutgoingDocumentStatus.Editing, edited.Value!.Status);
            }

            var hybridPass = await ReviewAsync(factory, documentId, staffId);
            Assert.True(hybridPass.Succeeded, $"{hybridPass.Failure}: {hybridPass.Detail}");
            Assert.NotNull(hybridPass.Value);
            Assert.Equal(ReviewSource.Hybrid, hybridPass.Value.ReviewSource);
            Assert.Equal(ReviewResult.Passed, hybridPass.Value.ReviewResult);
            Assert.DoesNotContain(hybridPass.Value.ReviewIssues, issue => issue.Severity == "Error");
            Assert.Equal(OutgoingDocumentStatus.PendingApproval, hybridPass.Value.DocumentStatus);
        }
        finally
        {
            await CleanupAsync(factory, documentId, templateId, documentTypeId);
        }
    }

    private static async Task<ReviewOperationResult<ReviewResponse>> ReviewAsync(
        WebApplicationFactory<Program> factory,
        Guid documentId,
        Guid staffId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IOutgoingDocumentReviewService>();
        return await service.CreateAsync(documentId, staffId);
    }

    private static async Task CleanupAsync(
        WebApplicationFactory<Program> factory,
        Guid documentId,
        Guid templateId,
        Guid documentTypeId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        try
        {
            var qdrant = scope.ServiceProvider.GetRequiredService<IQdrantKnowledgeClient>();
            var formatRulePoints = await qdrant.GetFormatRuleStatesAsync();
            await qdrant.DeleteFormatRulePointsAsync(
                formatRulePoints
                    .Where(point => point.TemplateId == templateId)
                    .Select(point => point.PointId)
                    .ToArray());
        }
        finally
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DigitalOpsDbContext>();
            await dbContext.ReviewHistory
                .Where(review => review.OutgoingDocumentId == documentId)
                .ExecuteDeleteAsync();
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
}

internal sealed class LiveAiReviewApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
        });
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DocumentCatalogSeed:Enabled"] = "false",
                ["IdentityBootstrap:Enabled"] = "false",
                ["ReminderWorker:Enabled"] = "false"
            }));
    }
}
