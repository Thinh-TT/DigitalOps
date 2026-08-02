using System.Text.Json;
using DigitalOps.API.Features.Drafting;
using DigitalOps.API.Features.OutgoingDocuments;
using DigitalOps.API.Shared.AI;
using DigitalOps.API.Shared.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DigitalOps.API.Tests;

public sealed class AiDraftGeneratorTests
{
    [Fact]
    public async Task Generator_syncs_only_active_templates_retrieves_exact_source_and_minimizes_prompt_data()
    {
        await using var database = await GeneratorDatabase.CreateAsync();
        var active = await database.CreateTemplateAsync(
            "PLAN",
            "Mẫu kế hoạch",
            "KẾ HOẠCH\nI. MỤC ĐÍCH\n" + string.Join(' ', Enumerable.Repeat("nội-dung", 600)),
            isActive: true);
        var inactive = await database.CreateTemplateAsync(
            "REPORT",
            "Mẫu ngừng hoạt động",
            "BÁO CÁO\nDữ liệu không được index",
            isActive: false);
        var stalePointId = Guid.NewGuid();
        var qdrant = new TemplateQdrantTestDouble
        {
            ExistingStates =
            [
                new TemplateKnowledgeState(
                    stalePointId,
                    Guid.NewGuid(),
                    "old",
                    "old:1",
                    "old-hash")
            ]
        };
        var embedding = new EmbeddingTestDouble();
        var chat = new ChatTestDouble
        {
            Handler = _ => new AiChatResult(
                JsonSerializer.Serialize(new
                {
                    content = "KẾ HOẠCH\nI. MỤC ĐÍCH\n[CẦN BỔ SUNG]",
                    sourceRefs = new[] { active.Id.ToString() }
                }),
                "Test",
                "test-model",
                100,
                20)
        };
        var gate = new GateTestDouble();
        var generator = CreateGenerator(database.Context, embedding, qdrant, chat, gate);

        var result = await generator.GenerateAsync(new AiDraftGenerationInput(
            active.Id,
            active.Name,
            active.DocumentType.Code,
            active.DocumentType.Name,
            "Kế hoạch thử nghiệm",
            "Nội dung đang lưu",
            new AiDraftMemberContext(
                "Nguyễn Văn A",
                new DateOnly(1990, 1, 2),
                "Nam",
                "Phường 1",
                "0900000000",
                "a@example.test",
                "Tổ trưởng",
                new DateOnly(2020, 5, 6)),
            new AiDraftIncomingContext(
                "01/CV",
                "UBND phường",
                "Triển khai nhiệm vụ",
                new DateOnly(2026, 8, 1),
                new DateOnly(2026, 8, 10)),
            "Nhấn mạnh tiến độ"));

        Assert.Contains("KẾ HOẠCH", result.Content, StringComparison.Ordinal);
        Assert.Contains(stalePointId, qdrant.DeletedPointIds);
        Assert.NotEmpty(qdrant.UpsertedPoints);
        Assert.All(qdrant.UpsertedPoints, point => Assert.Equal(active.Id, point.TemplateId));
        Assert.DoesNotContain(qdrant.UpsertedPoints, point => point.TemplateId == inactive.Id);
        Assert.True(qdrant.UpsertedPoints.Count > 1);
        Assert.All(qdrant.UpsertedPoints, point =>
            Assert.True(point.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 512));
        Assert.Equal(active.Id, qdrant.LastTemplateId);
        Assert.Equal("PLAN", qdrant.LastDocumentTypeCode);
        Assert.Equal(AiOperationKind.Draft, chat.LastRequest!.Operation);
        var prompt = chat.LastRequest.Messages.Single(message => message.Role == "user").Content;
        Assert.Contains("Nguyễn Văn A", prompt, StringComparison.Ordinal);
        Assert.Contains("Triển khai nhiệm vụ", prompt, StringComparison.Ordinal);
        Assert.Contains("Nhấn mạnh tiến độ", prompt, StringComparison.Ordinal);
        Assert.Contains("dữ liệu không tin cậy", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, gate.WaitCount);
        Assert.Equal(1, gate.ReleaseCount);
    }

    [Fact]
    public async Task Generator_rejects_unknown_source_reference_and_releases_gate()
    {
        await using var database = await GeneratorDatabase.CreateAsync();
        var template = await database.CreateTemplateAsync(
            "REPORT",
            "Mẫu báo cáo",
            "BÁO CÁO\nI. KẾT QUẢ",
            isActive: true);
        var qdrant = new TemplateQdrantTestDouble();
        var chat = new ChatTestDouble
        {
            Handler = _ => new AiChatResult(
                JsonSerializer.Serialize(new
                {
                    content = "BÁO CÁO\nI. KẾT QUẢ",
                    sourceRefs = new[] { Guid.NewGuid().ToString() }
                }),
                "Test",
                "test-model",
                null,
                null)
        };
        var gate = new GateTestDouble();
        var generator = CreateGenerator(
            database.Context,
            new EmbeddingTestDouble(),
            qdrant,
            chat,
            gate);

        var exception = await Assert.ThrowsAsync<AiProviderException>(() =>
            generator.GenerateAsync(new AiDraftGenerationInput(
                template.Id,
                template.Name,
                template.DocumentType.Code,
                template.DocumentType.Name,
                "Báo cáo",
                "Nội dung",
                null,
                null,
                null)));

        Assert.Contains("schema and guardrails", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, gate.ReleaseCount);
    }

    private static AiDraftGenerator CreateGenerator(
        DigitalOpsDbContext context,
        IEmbeddingClient embedding,
        IQdrantKnowledgeClient qdrant,
        IAiChatClient chat,
        IAiOperationGate gate) =>
        new(
            context,
            embedding,
            qdrant,
            chat,
            gate,
            Options.Create(new AiProviderOptions
            {
                TimeoutSeconds = 60,
                Qdrant = new QdrantAiOptions { ApiKey = "test-key" }
            }),
            NullLogger<AiDraftGenerator>.Instance);

    private sealed class EmbeddingTestDouble : IEmbeddingClient
    {
        public string Provider => "Test";

        public string Model => "test-embedding";

        public List<IReadOnlyList<string>> Inputs { get; } = [];

        public Task<IReadOnlyList<float[]>> EmbedAsync(
            IReadOnlyList<string> inputs,
            CancellationToken cancellationToken = default)
        {
            Inputs.Add(inputs);
            return Task.FromResult<IReadOnlyList<float[]>>(
                inputs.Select(_ => new float[1024]).ToArray());
        }
    }

    private sealed class ChatTestDouble : IAiChatClient
    {
        public string Provider => "Test";

        public string Model => "test-chat";

        public Func<AiChatRequest, AiChatResult> Handler { get; set; } =
            _ => throw new InvalidOperationException("A handler is required.");

        public AiChatRequest? LastRequest { get; private set; }

        public Task<AiChatResult> CompleteAsync(
            AiChatRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(Handler(request));
        }
    }

    private sealed class GateTestDouble : IAiOperationGate
    {
        public int WaitCount { get; private set; }

        public int ReleaseCount { get; private set; }

        public Task WaitAsync(CancellationToken cancellationToken = default)
        {
            WaitCount++;
            return Task.CompletedTask;
        }

        public void Release() => ReleaseCount++;
    }

    private sealed class TemplateQdrantTestDouble : IQdrantKnowledgeClient
    {
        public IReadOnlyList<TemplateKnowledgeState> ExistingStates { get; set; } = [];

        public List<TemplateKnowledgePoint> UpsertedPoints { get; } = [];

        public IReadOnlyList<Guid> DeletedPointIds { get; private set; } = [];

        public Guid? LastTemplateId { get; private set; }

        public string? LastDocumentTypeCode { get; private set; }

        public Task EnsureCollectionAsync(
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyDictionary<Guid, string>> GetStaffContentHashesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(
                new Dictionary<Guid, string>());

        public Task UpsertStaffPointsAsync(
            IReadOnlyList<StaffKnowledgePoint> points,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteStaffPointsAsync(
            IReadOnlyList<Guid> staffIds,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<StaffKnowledgeCandidate>> SearchStaffAsync(
            float[] queryVector,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StaffKnowledgeCandidate>>([]);

        public Task<IReadOnlyList<TemplateKnowledgeState>> GetTemplateStatesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ExistingStates);

        public Task UpsertTemplatePointsAsync(
            IReadOnlyList<TemplateKnowledgePoint> points,
            CancellationToken cancellationToken = default)
        {
            UpsertedPoints.AddRange(points);
            return Task.CompletedTask;
        }

        public Task DeleteTemplatePointsAsync(
            IReadOnlyList<Guid> pointIds,
            CancellationToken cancellationToken = default)
        {
            DeletedPointIds = pointIds;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TemplateKnowledgeCandidate>> SearchTemplateAsync(
            float[] queryVector,
            Guid templateId,
            string documentTypeCode,
            CancellationToken cancellationToken = default)
        {
            LastTemplateId = templateId;
            LastDocumentTypeCode = documentTypeCode;
            return Task.FromResult<IReadOnlyList<TemplateKnowledgeCandidate>>(
                UpsertedPoints
                    .Where(point => point.TemplateId == templateId
                        && point.DocumentTypeCode == documentTypeCode)
                    .Select(point => new TemplateKnowledgeCandidate(
                        point.PointId,
                        point.TemplateId,
                        point.DocumentTypeCode,
                        point.SourceVersion,
                        point.ChunkId,
                        point.ContentHash,
                        point.Content,
                        0.9))
                    .Take(5)
                    .ToArray());
        }

        public Task<IReadOnlyList<FormatRuleKnowledgeState>> GetFormatRuleStatesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FormatRuleKnowledgeState>>([]);

        public Task UpsertFormatRulePointsAsync(
            IReadOnlyList<FormatRuleKnowledgePoint> points,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteFormatRulePointsAsync(
            IReadOnlyList<Guid> pointIds,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<FormatRuleKnowledgeCandidate>> SearchFormatRulesAsync(
            float[] queryVector,
            Guid templateId,
            string documentTypeCode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FormatRuleKnowledgeCandidate>>([]);
    }

    private sealed class GeneratorDatabase(
        SqliteConnection connection,
        DigitalOpsDbContext context) : IAsyncDisposable
    {
        public DigitalOpsDbContext Context { get; } = context;

        public static async Task<GeneratorDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<DigitalOpsDbContext>()
                .UseSqlite(connection)
                .ReplaceService<IModelCustomizer, AuthenticationTestModelCustomizer>()
                .Options;
            var context = new DigitalOpsDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new GeneratorDatabase(connection, context);
        }

        public async Task<DocumentTemplate> CreateTemplateAsync(
            string code,
            string name,
            string content,
            bool isActive)
        {
            var type = new DocumentType
            {
                Id = Guid.NewGuid(),
                Code = code,
                Name = code,
                IsActive = true
            };
            var template = new DocumentTemplate
            {
                Id = Guid.NewGuid(),
                DocumentTypeId = type.Id,
                DocumentType = type,
                Name = name,
                TemplateContent = content,
                FormatRules = JsonDocument.Parse("{\"version\":1,\"rules\":[]}")
                    .RootElement.Clone(),
                IsActive = isActive
            };
            Context.DocumentTemplates.Add(template);
            await Context.SaveChangesAsync();
            return template;
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
