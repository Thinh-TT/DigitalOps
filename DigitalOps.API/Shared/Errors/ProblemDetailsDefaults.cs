using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace DigitalOps.API.Shared.Errors;

public static class ProblemDetailsDefaults
{
    public const string TraceIdKey = "traceId";

    public static void Apply(HttpContext httpContext, ProblemDetails problemDetails)
    {
        var statusCode = problemDetails.Status ?? httpContext.Response.StatusCode;
        if (statusCode < StatusCodes.Status400BadRequest)
        {
            statusCode = StatusCodes.Status500InternalServerError;
        }

        var definition = ProblemDetailsCatalog.Get(statusCode);
        var hasFrameworkDefaultType =
            string.IsNullOrWhiteSpace(problemDetails.Type)
            || problemDetails.Type.StartsWith(
                "https://tools.ietf.org/",
                StringComparison.OrdinalIgnoreCase)
            || problemDetails.Type.StartsWith(
                "https://www.rfc-editor.org/",
                StringComparison.OrdinalIgnoreCase);

        problemDetails.Status = statusCode;
        if (hasFrameworkDefaultType)
        {
            problemDetails.Type = definition.Type;
            if (HasFrameworkDefaultTitle(problemDetails.Title, statusCode))
            {
                problemDetails.Title = definition.Title;
            }
        }
        else
        {
            problemDetails.Title ??= definition.Title;
        }

        problemDetails.Instance ??= GetRequestPath(httpContext.Request);
        problemDetails.Extensions.TryAdd(
            TraceIdKey,
            Activity.Current?.Id ?? httpContext.TraceIdentifier);
    }

    private static bool HasFrameworkDefaultTitle(string? title, int statusCode) =>
        string.IsNullOrWhiteSpace(title)
        || string.Equals(
            title,
            ReasonPhrases.GetReasonPhrase(statusCode),
            StringComparison.Ordinal)
        || statusCode == StatusCodes.Status500InternalServerError
        && string.Equals(
            title,
            "An error occurred while processing your request.",
            StringComparison.Ordinal);

    private static string GetRequestPath(HttpRequest request) =>
        request.PathBase.Add(request.Path).Value ?? "/";
}
