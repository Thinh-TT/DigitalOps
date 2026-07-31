using Microsoft.Extensions.Options;

namespace DigitalOps.API.Shared.Identity;

public sealed class IdentityBootstrapOptions
{
    public const string SectionName = "IdentityBootstrap";

    public bool Enabled { get; init; }

    public string? UserName { get; init; }

    public string? Email { get; init; }

    public string? TemporaryPassword { get; init; }

    public string? FullName { get; init; }

    public string? Position { get; init; }

    public string? Department { get; init; }

    public string? Phone { get; init; }
}

public sealed class IdentityBootstrapOptionsValidator
    : IValidateOptions<IdentityBootstrapOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        IdentityBootstrapOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var missingFields = new[]
            {
                (nameof(options.UserName), options.UserName),
                (nameof(options.Email), options.Email),
                (nameof(options.TemporaryPassword), options.TemporaryPassword),
                (nameof(options.FullName), options.FullName)
            }
            .Where(field => string.IsNullOrWhiteSpace(field.Item2))
            .Select(field => field.Item1)
            .ToArray();

        return missingFields.Length == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"IdentityBootstrap requires: {string.Join(", ", missingFields)}.");
    }
}
