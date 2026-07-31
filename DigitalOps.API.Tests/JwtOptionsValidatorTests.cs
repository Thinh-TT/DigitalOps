using DigitalOps.API.Shared.Identity;

namespace DigitalOps.API.Tests;

public sealed class JwtOptionsValidatorTests
{
    private readonly JwtOptionsValidator _validator = new();

    [Fact]
    public void Valid_configuration_succeeds()
    {
        var result = _validator.Validate(null, CreateValidOptions());

        Assert.True(result.Succeeded);
    }

    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public void Invalid_configuration_fails(JwtOptions options, string expectedFailure)
    {
        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains(expectedFailure, StringComparison.Ordinal));
    }

    public static IEnumerable<object[]> InvalidOptions()
    {
        var missingIssuer = CreateValidOptions();
        missingIssuer.Issuer = " ";
        yield return [missingIssuer, "Issuer is required"];

        var missingAudience = CreateValidOptions();
        missingAudience.Audience = string.Empty;
        yield return [missingAudience, "Audience is required"];

        var shortSigningKey = CreateValidOptions();
        shortSigningKey.SigningKey = "too-short";
        yield return [shortSigningKey, "at least 32 UTF-8 bytes"];

        var invalidLifetime = CreateValidOptions();
        invalidLifetime.AccessTokenLifetimeMinutes = 0;
        yield return [invalidLifetime, "must be greater than zero"];
    }

    private static JwtOptions CreateValidOptions() => new()
    {
        Issuer = "DigitalOps.API",
        Audience = "DigitalOps.Web",
        SigningKey = "0123456789abcdef0123456789abcdef",
        AccessTokenLifetimeMinutes = 480
    };
}
