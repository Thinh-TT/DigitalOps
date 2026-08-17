namespace DigitalOps.RagIngestion.Tests;

public sealed class CommandLineTests
{
    [Theory]
    [InlineData("validate", "Validate")]
    [InlineData("plan", "Plan")]
    public void Parse_accepts_standard_commands(
        string command,
        string expectedOperation)
    {
        var result = CommandLine.Parse(
            [command, "--staging-dir", "C:\\staging\\JOB"]);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Options);
        Assert.Equal(expectedOperation, result.Options.Operation.ToString());
        Assert.False(result.Options.UsesLegacySyntax);
    }

    [Fact]
    public void Parse_accepts_governed_publish()
    {
        var result = CommandLine.Parse(
            [
                "publish",
                "--staging-dir",
                "C:\\staging\\JOB",
                "--source-registry",
                "C:\\config\\source-registry.json"
            ]);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            RagIngestionOperation.Publish,
            result.Options!.Operation);
    }

    [Fact]
    public void Parse_accepts_admit_with_audit_identity()
    {
        var result = CommandLine.Parse(
            [
                "admit",
                "--staging-dir",
                "C:\\staging\\JOB",
                "--source-registry",
                "C:\\config\\source-registry.json",
                "--approved-by",
                "Project Owner",
                "--approval-reference",
                "T4-03-approval"
            ]);

        Assert.True(result.IsSuccess);
        Assert.Equal(RagIngestionOperation.Admit, result.Options!.Operation);
    }

    [Theory]
    [InlineData("--validate-only", "Validate")]
    [InlineData("--dry-run", "Plan")]
    public void Parse_keeps_legacy_flags_as_compatibility_aliases(
        string flag,
        string expectedOperation)
    {
        var result = CommandLine.Parse(
            ["--staging-dir", "C:\\staging\\JOB", flag]);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Options);
        Assert.Equal(expectedOperation, result.Options.Operation.ToString());
        Assert.True(result.Options.UsesLegacySyntax);
    }

    [Theory]
    [InlineData("validate", "--resume")]
    [InlineData("publish", "--validate-only")]
    [InlineData("validate", "--dry-run")]
    public void Parse_rejects_conflicting_modes(string command, string flag)
    {
        var result = CommandLine.Parse(
            [command, "--staging-dir", "C:\\staging\\JOB", flag]);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Parse_rejects_unknown_arguments()
    {
        var result = CommandLine.Parse(
            ["validate", "--staging-dir", "C:\\staging\\JOB", "--unknown"]);

        Assert.False(result.IsSuccess);
        Assert.Contains("Unknown argument", result.Error, StringComparison.Ordinal);
    }
}
