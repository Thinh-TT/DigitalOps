using System.Text;
using Microsoft.Extensions.Options;

namespace DigitalOps.API.Shared.Identity;

public sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    private const int MinimumSigningKeyBytes = 32;

    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            failures.Add($"{JwtOptions.SectionName}:Issuer is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            failures.Add($"{JwtOptions.SectionName}:Audience is required.");
        }

        if (Encoding.UTF8.GetByteCount(options.SigningKey ?? string.Empty) < MinimumSigningKeyBytes)
        {
            failures.Add(
                $"{JwtOptions.SectionName}:SigningKey must contain at least {MinimumSigningKeyBytes} UTF-8 bytes.");
        }

        if (options.AccessTokenLifetimeMinutes <= 0)
        {
            failures.Add($"{JwtOptions.SectionName}:AccessTokenLifetimeMinutes must be greater than zero.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
