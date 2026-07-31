using Microsoft.Extensions.Options;

namespace DigitalOps.API.Features.Members;

public sealed class MemberImportOptions
{
    public const string SectionName = "MemberImport";
    public const long DefaultMaxFileSizeBytes = 10 * 1024 * 1024;
    public const int DefaultMaxRows = 10_000;
    public const long DefaultMaxExpandedWorkbookBytes = 100 * 1024 * 1024;

    public long MaxFileSizeBytes { get; set; } = DefaultMaxFileSizeBytes;

    public int MaxRows { get; set; } = DefaultMaxRows;

    public long MaxExpandedWorkbookBytes { get; set; } =
        DefaultMaxExpandedWorkbookBytes;
}

public sealed class MemberImportOptionsValidator
    : IValidateOptions<MemberImportOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        MemberImportOptions options)
    {
        var failures = new List<string>();

        if (options.MaxFileSizeBytes <= 0)
        {
            failures.Add("MemberImport:MaxFileSizeBytes must be greater than zero.");
        }

        if (options.MaxRows <= 0)
        {
            failures.Add("MemberImport:MaxRows must be greater than zero.");
        }
        else if (options.MaxRows > 1_048_575)
        {
            failures.Add(
                "MemberImport:MaxRows cannot exceed 1,048,575 Excel data rows.");
        }

        if (options.MaxExpandedWorkbookBytes < options.MaxFileSizeBytes)
        {
            failures.Add(
                "MemberImport:MaxExpandedWorkbookBytes must be greater than or equal to MaxFileSizeBytes.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
