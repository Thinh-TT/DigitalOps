using System.Text.Json;

namespace DigitalOps.API.Shared.AI;

public sealed record AiChatMessage(
    string Role,
    string Content);

public sealed record AiJsonSchema(
    string Name,
    JsonElement Schema);

public enum AiOperationKind
{
    Assignment,
    Draft,
    Review
}

public sealed record AiChatRequest(
    AiOperationKind Operation,
    IReadOnlyList<AiChatMessage> Messages,
    AiJsonSchema Schema);

public sealed record AiChatResult(
    string Content,
    string Provider,
    string Model,
    int? PromptTokens,
    int? OutputTokens);

public interface IAiChatClient
{
    string Provider { get; }

    string Model { get; }

    Task<AiChatResult> CompleteAsync(
        AiChatRequest request,
        CancellationToken cancellationToken = default);
}

public interface IEmbeddingClient
{
    string Provider { get; }

    string Model { get; }

    Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default);
}

public sealed record StaffKnowledgePoint(
    Guid StaffId,
    string SourceVersion,
    string ChunkId,
    string ContentHash,
    string Content,
    float[] Vector,
    DateTime IndexedAtUtc);

public sealed record StaffKnowledgeCandidate(
    Guid StaffId,
    string ContentHash,
    string Content,
    double Score);

public sealed record TemplateKnowledgePoint(
    Guid PointId,
    Guid TemplateId,
    string DocumentTypeCode,
    string SourceVersion,
    string ChunkId,
    string ContentHash,
    string Content,
    float[] Vector,
    DateTime IndexedAtUtc);

public sealed record TemplateKnowledgeState(
    Guid PointId,
    Guid TemplateId,
    string SourceVersion,
    string ChunkId,
    string ContentHash);

public sealed record TemplateKnowledgeCandidate(
    Guid PointId,
    Guid TemplateId,
    string DocumentTypeCode,
    string SourceVersion,
    string ChunkId,
    string ContentHash,
    string Content,
    double Score);

public sealed record FormatRuleKnowledgePoint(
    Guid PointId,
    Guid TemplateId,
    string DocumentTypeCode,
    string RuleCode,
    string SourceVersion,
    string ChunkId,
    string ContentHash,
    string Content,
    float[] Vector,
    DateTime IndexedAtUtc);

public sealed record FormatRuleKnowledgeState(
    Guid PointId,
    Guid TemplateId,
    string SourceVersion,
    string ChunkId,
    string ContentHash);

public sealed record FormatRuleKnowledgeCandidate(
    Guid PointId,
    Guid TemplateId,
    string DocumentTypeCode,
    string RuleCode,
    string SourceVersion,
    string ChunkId,
    string ContentHash,
    string Content,
    double Score);

public interface IQdrantKnowledgeClient
{
    Task EnsureCollectionAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, string>> GetStaffContentHashesAsync(
        CancellationToken cancellationToken = default);

    Task UpsertStaffPointsAsync(
        IReadOnlyList<StaffKnowledgePoint> points,
        CancellationToken cancellationToken = default);

    Task DeleteStaffPointsAsync(
        IReadOnlyList<Guid> staffIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StaffKnowledgeCandidate>> SearchStaffAsync(
        float[] queryVector,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TemplateKnowledgeState>> GetTemplateStatesAsync(
        CancellationToken cancellationToken = default);

    Task UpsertTemplatePointsAsync(
        IReadOnlyList<TemplateKnowledgePoint> points,
        CancellationToken cancellationToken = default);

    Task DeleteTemplatePointsAsync(
        IReadOnlyList<Guid> pointIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TemplateKnowledgeCandidate>> SearchTemplateAsync(
        float[] queryVector,
        Guid templateId,
        string documentTypeCode,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FormatRuleKnowledgeState>> GetFormatRuleStatesAsync(
        CancellationToken cancellationToken = default);

    Task UpsertFormatRulePointsAsync(
        IReadOnlyList<FormatRuleKnowledgePoint> points,
        CancellationToken cancellationToken = default);

    Task DeleteFormatRulePointsAsync(
        IReadOnlyList<Guid> pointIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FormatRuleKnowledgeCandidate>> SearchFormatRulesAsync(
        float[] queryVector,
        Guid templateId,
        string documentTypeCode,
        CancellationToken cancellationToken = default);
}

public interface IAiOperationGate
{
    Task WaitAsync(CancellationToken cancellationToken = default);

    void Release();
}

public sealed class AiOperationGate : IAiOperationGate, IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public Task WaitAsync(CancellationToken cancellationToken = default) =>
        _semaphore.WaitAsync(cancellationToken);

    public void Release() => _semaphore.Release();

    public void Dispose() => _semaphore.Dispose();
}

public sealed class AiProviderException(
    string message,
    int? statusCode = null,
    Exception? innerException = null) : Exception(message, innerException)
{
    public int? StatusCode { get; } = statusCode;
}

internal static class AiRequestSettings
{
    public static (int MaxOutputTokens, double Temperature) Resolve(
        AiOperationKind operation,
        AiProviderOptions options) => operation switch
        {
            AiOperationKind.Assignment =>
                (options.AssignmentMaxOutputTokens, 0),
            AiOperationKind.Draft =>
                (options.DraftMaxOutputTokens, 0.2),
            AiOperationKind.Review =>
                (options.ReviewMaxOutputTokens, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        };
}
