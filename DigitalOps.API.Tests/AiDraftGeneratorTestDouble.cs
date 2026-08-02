using DigitalOps.API.Features.OutgoingDocuments;

namespace DigitalOps.API.Tests;

internal sealed class AiDraftGeneratorTestDouble : IAiDraftGenerator
{
    public Func<
        AiDraftGenerationInput,
        CancellationToken,
        Task<AiDraftGenerationResult>>
        Handler
    { get; set; } =
        (_, _) => Task.FromResult(new AiDraftGenerationResult("Bản nháp AI thử nghiệm"));

    public int CallCount { get; private set; }

    public AiDraftGenerationInput? LastInput { get; private set; }

    public Task<AiDraftGenerationResult> GenerateAsync(
        AiDraftGenerationInput input,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastInput = input;
        return Handler(input, cancellationToken);
    }
}
