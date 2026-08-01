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
