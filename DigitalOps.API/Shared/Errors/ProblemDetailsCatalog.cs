using Microsoft.AspNetCore.WebUtilities;

namespace DigitalOps.API.Shared.Errors;

public static class ProblemDetailsCatalog
{
    public const string TypeBaseUri = "https://digitalops/errors/";

    public static readonly ProblemDetailsDefinition PasswordChangeRequired =
        new(
            "password-change-required",
            "Password change is required.");

    private static readonly IReadOnlyDictionary<int, ProblemDetailsDefinition> Definitions =
        new Dictionary<int, ProblemDetailsDefinition>
        {
            [StatusCodes.Status400BadRequest] =
                new("validation-error", "One or more validation errors occurred."),
            [StatusCodes.Status401Unauthorized] =
                new("unauthorized", "Authentication is required."),
            [StatusCodes.Status403Forbidden] =
                new("forbidden", "Access is forbidden."),
            [StatusCodes.Status404NotFound] =
                new("not-found", "The requested resource was not found."),
            [StatusCodes.Status409Conflict] =
                new("conflict", "The request conflicts with the current resource state."),
            [StatusCodes.Status413PayloadTooLarge] =
                new("file-too-large", "The request payload is too large."),
            [StatusCodes.Status415UnsupportedMediaType] =
                new("unsupported-file-type", "The request media type is not supported."),
            [StatusCodes.Status422UnprocessableEntity] =
                new("business-validation-failed", "One or more business validation errors occurred."),
            [StatusCodes.Status500InternalServerError] =
                new("internal-server-error", "An unexpected error occurred."),
            [StatusCodes.Status503ServiceUnavailable] =
                new("ai-service-unavailable", "The AI service is unavailable.")
        };

    public static ProblemDetailsDefinition Get(int statusCode)
    {
        if (Definitions.TryGetValue(statusCode, out var definition))
        {
            return definition;
        }

        var reasonPhrase = ReasonPhrases.GetReasonPhrase(statusCode);
        return new ProblemDetailsDefinition(
            $"http-{statusCode}",
            string.IsNullOrWhiteSpace(reasonPhrase) ? "The request failed." : reasonPhrase);
    }

    public sealed record ProblemDetailsDefinition(string Code, string Title)
    {
        public string Type => $"{TypeBaseUri}{Code}";
    }
}
