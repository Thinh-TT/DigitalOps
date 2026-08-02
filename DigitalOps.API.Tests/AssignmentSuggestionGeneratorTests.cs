using DigitalOps.API.Features.IncomingDocuments;
using DigitalOps.API.Shared.AI;
using DigitalOps.API.Shared.Data;
using DigitalOps.API.Shared.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DigitalOps.API.Tests;

public sealed class AssignmentSuggestionGeneratorTests
{
    [Fact]
    public async Task Generator_synchronizes_changed_staff_deletes_stale_and_validates_chat_choice()
    {
        await using var database = await GeneratorDatabase.CreateAsync();
        var active = await database.CreateStaffAsync(
            "Nguyễn Văn Tuyên",
            "Cán bộ tuyên truyền",
            "Ban Tuyên giáo",
            isActive: true,
            SystemRoles.Drafter);
        await database.CreateStaffAsync(
            "Nhân sự đã nghỉ",
            "Cán bộ cũ",
            "Văn phòng",
            isActive: false,
            SystemRoles.Clerk);
        var staleId = Guid.NewGuid();
        var qdrant = new QdrantTestDouble
        {
            ExistingHashes = new Dictionary<Guid, string>
            {
                [active.Id] = "old-hash",
                [staleId] = "stale-hash"
            }
        };
        var chat = new ChatTestDouble
        {
            Handler = request => new AiChatResult(
                $"{{\"decision\":\"Suggested\",\"suggestedStaffId\":\"{active.Id:D}\",\"reason\":\"Phù hợp công tác tuyên truyền.\",\"sourceRefs\":[\"{active.Id:D}\"]}}",
                AiProviderNames.Ollama,
                OllamaAiOptions.ApprovedLlmModel,
                10,
                5)
        };
        using var gate = new AiOperationGate();
        var generator = new AssignmentSuggestionGenerator(
            database.Context,
            new EmbeddingTestDouble(),
            qdrant,
            chat,
            gate,
            Options.Create(CreateOptions()),
            TimeProvider.System,
            NullLogger<AssignmentSuggestionGenerator>.Instance);

        var result = await generator.SuggestAsync(new AssignmentSuggestionInput(
            "Xây dựng nội dung tuyên truyền về đại đoàn kết",
            "REPORT",
            "Báo cáo"));

        Assert.Equal(AssignmentSuggestionDecisionKind.Suggested, result.Decision);
        Assert.Equal(active.Id, result.SuggestedStaffId);
        var point = Assert.Single(qdrant.UpsertedPoints);
        Assert.Equal(active.Id, point.StaffId);
        Assert.Contains("Nguyễn Văn Tuyên", point.Content, StringComparison.Ordinal);
        Assert.Contains(SystemRoles.Drafter, point.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("secret@example.local", point.Content, StringComparison.Ordinal);
        Assert.Equal([staleId], qdrant.DeletedIds);
        Assert.Equal(1, chat.CallCount);
        Assert.Contains("sourceId=", chat.LastRequest!.Messages[1].Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generator_abstains_without_chat_when_retrieval_has_no_candidate()
    {
        await using var database = await GeneratorDatabase.CreateAsync();
        await database.CreateStaffAsync(
            "Cán bộ văn thư",
            "Văn thư",
            "Văn phòng",
            isActive: true,
            SystemRoles.Clerk);
        var qdrant = new QdrantTestDouble { ReturnNoCandidates = true };
        var chat = new ChatTestDouble();
        using var gate = new AiOperationGate();
        var generator = new AssignmentSuggestionGenerator(
            database.Context,
            new EmbeddingTestDouble(),
            qdrant,
            chat,
            gate,
            Options.Create(CreateOptions()),
            TimeProvider.System,
            NullLogger<AssignmentSuggestionGenerator>.Instance);

        var result = await generator.SuggestAsync(new AssignmentSuggestionInput(
            "Nội dung không đủ thông tin",
            "OTHER",
            "Khác"));

        Assert.Equal(
            AssignmentSuggestionDecisionKind.InsufficientEvidence,
            result.Decision);
        Assert.Null(result.SuggestedStaffId);
        Assert.Equal(0, chat.CallCount);
    }

    [Fact]
    public async Task Generator_rejects_out_of_candidate_source_reference()
    {
        await using var database = await GeneratorDatabase.CreateAsync();
        await database.CreateStaffAsync(
            "Cán bộ văn thư",
            "Văn thư",
            "Văn phòng",
            isActive: true,
            SystemRoles.Clerk);
        var qdrant = new QdrantTestDouble();
        var invalidId = Guid.NewGuid();
        var chat = new ChatTestDouble
        {
            Handler = _ => new AiChatResult(
                $"{{\"decision\":\"Suggested\",\"suggestedStaffId\":\"{invalidId:D}\",\"reason\":\"Không hợp lệ\",\"sourceRefs\":[\"{invalidId:D}\"]}}",
                AiProviderNames.Ollama,
                OllamaAiOptions.ApprovedLlmModel,
                null,
                null)
        };
        using var gate = new AiOperationGate();
        var generator = new AssignmentSuggestionGenerator(
            database.Context,
            new EmbeddingTestDouble(),
            qdrant,
            chat,
            gate,
            Options.Create(CreateOptions()),
            TimeProvider.System,
            NullLogger<AssignmentSuggestionGenerator>.Instance);

        await Assert.ThrowsAsync<AiProviderException>(() =>
            generator.SuggestAsync(new AssignmentSuggestionInput(
                "Báo cáo văn thư",
                "REPORT",
                "Báo cáo")));
    }

    private static AiProviderOptions CreateOptions() => new()
    {
        Qdrant = new QdrantAiOptions { ApiKey = "test-key" }
    };

    private sealed class EmbeddingTestDouble : IEmbeddingClient
    {
        public string Provider => AiProviderNames.Ollama;

        public string Model => EmbeddingAiOptions.ApprovedModel;

        public Task<IReadOnlyList<float[]>> EmbedAsync(
            IReadOnlyList<string> inputs,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<float[]>>(
                inputs.Select(_ => new float[1024]).ToArray());
    }

    private sealed class ChatTestDouble : IAiChatClient
    {
        public Func<AiChatRequest, AiChatResult> Handler { get; set; } =
            _ => throw new InvalidOperationException("Chat should not be called.");

        public int CallCount { get; private set; }

        public AiChatRequest? LastRequest { get; private set; }

        public string Provider => AiProviderNames.Ollama;

        public string Model => OllamaAiOptions.ApprovedLlmModel;

        public Task<AiChatResult> CompleteAsync(
            AiChatRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(Handler(request));
        }
    }

    private sealed class QdrantTestDouble : IQdrantKnowledgeClient
    {
        public IReadOnlyDictionary<Guid, string> ExistingHashes { get; set; } =
            new Dictionary<Guid, string>();

        public bool ReturnNoCandidates { get; set; }

        public List<StaffKnowledgePoint> UpsertedPoints { get; } = [];

        public IReadOnlyList<Guid> DeletedIds { get; private set; } = [];

        public Task EnsureCollectionAsync(
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyDictionary<Guid, string>> GetStaffContentHashesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ExistingHashes);

        public Task UpsertStaffPointsAsync(
            IReadOnlyList<StaffKnowledgePoint> points,
            CancellationToken cancellationToken = default)
        {
            UpsertedPoints.AddRange(points);
            return Task.CompletedTask;
        }

        public Task DeleteStaffPointsAsync(
            IReadOnlyList<Guid> staffIds,
            CancellationToken cancellationToken = default)
        {
            DeletedIds = staffIds;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<StaffKnowledgeCandidate>> SearchStaffAsync(
            float[] queryVector,
            CancellationToken cancellationToken = default)
        {
            if (ReturnNoCandidates)
            {
                return Task.FromResult<IReadOnlyList<StaffKnowledgeCandidate>>([]);
            }

            var candidates = UpsertedPoints
                .Select(point => new StaffKnowledgeCandidate(
                    point.StaffId,
                    point.ContentHash,
                    point.Content,
                    0.9))
                .ToArray();
            return Task.FromResult<IReadOnlyList<StaffKnowledgeCandidate>>(candidates);
        }

        public Task<IReadOnlyList<TemplateKnowledgeState>> GetTemplateStatesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TemplateKnowledgeState>>([]);

        public Task UpsertTemplatePointsAsync(
            IReadOnlyList<TemplateKnowledgePoint> points,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteTemplatePointsAsync(
            IReadOnlyList<Guid> pointIds,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<TemplateKnowledgeCandidate>> SearchTemplateAsync(
            float[] queryVector,
            Guid templateId,
            string documentTypeCode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TemplateKnowledgeCandidate>>([]);

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

        public async Task<Staff> CreateStaffAsync(
            string fullName,
            string position,
            string department,
            bool isActive,
            string roleName)
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = $"user-{Guid.NewGuid():N}",
                Email = "secret@example.local"
            };
            var role = await Context.Roles.SingleOrDefaultAsync(
                item => item.Name == roleName);
            if (role is null)
            {
                role = new IdentityRole<Guid>
                {
                    Id = Guid.NewGuid(),
                    Name = roleName,
                    NormalizedName = roleName.ToUpperInvariant()
                };
                Context.Roles.Add(role);
            }

            var staff = new Staff
            {
                Id = Guid.NewGuid(),
                IdentityUserId = user.Id,
                IdentityUser = user,
                FullName = fullName,
                Position = position,
                Department = department,
                Email = user.Email!,
                Phone = "0900000000",
                IsActive = isActive
            };
            Context.Staff.Add(staff);
            Context.UserRoles.Add(new IdentityUserRole<Guid>
            {
                UserId = user.Id,
                RoleId = role.Id
            });
            await Context.SaveChangesAsync();
            return staff;
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
