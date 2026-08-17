using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using DigitalOps.API.Shared.AI.Retrieval;

namespace DigitalOps.API.Shared.AI;

public static class AiServiceCollectionExtensions
{
    public static IServiceCollection AddDigitalOpsAi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<AiProviderOptions>()
            .Bind(configuration.GetSection(AiProviderOptions.SectionName))
            .ValidateOnStart();
        services.TryAddSingleton<
            IValidateOptions<AiProviderOptions>,
            AiProviderOptionsValidator>();

        services.AddHttpClient<OllamaAiChatClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
        });
        services.AddHttpClient<OllamaEmbeddingClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
        });
        services.AddHttpClient<ExternalAiChatClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
        });
        services.AddHttpClient<IQdrantKnowledgeClient, QdrantKnowledgeClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        services.AddScoped<IAiChatClient>(serviceProvider =>
        {
            var options = serviceProvider
                .GetRequiredService<IOptions<AiProviderOptions>>()
                .Value;

            return string.Equals(
                options.Provider,
                AiProviderNames.External,
                StringComparison.OrdinalIgnoreCase)
                ? serviceProvider.GetRequiredService<ExternalAiChatClient>()
                : serviceProvider.GetRequiredService<OllamaAiChatClient>();
        });
        services.AddScoped<IEmbeddingClient>(serviceProvider =>
            serviceProvider.GetRequiredService<OllamaEmbeddingClient>());
        services.TryAddSingleton<IAiOperationGate, AiOperationGate>();
        services.TryAddScoped<IRAGRetrievalService, RAGRetrievalService>();
        services.TryAddScoped<ICitationSnapshotService, CitationSnapshotService>();

        return services;
    }
}
