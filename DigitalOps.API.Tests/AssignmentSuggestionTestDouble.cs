using DigitalOps.API.Features.IncomingDocuments;

namespace DigitalOps.API.Tests;

internal sealed class AssignmentSuggestionTestDouble : IAssignmentSuggestionGenerator
{
    public Func<
        AssignmentSuggestionInput,
        CancellationToken,
        Task<AssignmentSuggestionDecision>> Handler
    { get; set; } =
        (_, _) => Task.FromResult(new AssignmentSuggestionDecision(
            AssignmentSuggestionDecisionKind.InsufficientEvidence,
            null,
            "Không đủ bằng chứng."));

    public int CallCount { get; private set; }

    public AssignmentSuggestionInput? LastInput { get; private set; }

    public Task<AssignmentSuggestionDecision> SuggestAsync(
        AssignmentSuggestionInput input,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastInput = input;
        return Handler(input, cancellationToken);
    }
}
