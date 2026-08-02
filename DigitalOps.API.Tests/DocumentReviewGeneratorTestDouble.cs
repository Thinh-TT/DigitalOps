using DigitalOps.API.Features.Review;

namespace DigitalOps.API.Tests;

internal sealed class DocumentReviewGeneratorTestDouble : IDocumentReviewGenerator
{
    public Func<
        DocumentReviewInput,
        CancellationToken,
        Task<DocumentReviewGenerationResult>>
        Handler
    { get; set; } =
        (_, _) => Task.FromResult(new DocumentReviewGenerationResult(
            ReviewSource.Hybrid,
            []));

    public int CallCount { get; private set; }

    public DocumentReviewInput? LastInput { get; private set; }

    public Task<DocumentReviewGenerationResult> ReviewAsync(
        DocumentReviewInput input,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastInput = input;
        return Handler(input, cancellationToken);
    }
}
