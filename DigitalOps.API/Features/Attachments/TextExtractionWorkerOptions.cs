using Microsoft.Extensions.Options;

namespace DigitalOps.API.Features.Attachments;

public sealed class TextExtractionWorkerOptions
{
    public const string SectionName = "TextExtractionWorker";

    public bool Enabled { get; set; } = true;

    public int PollIntervalSeconds { get; set; } = 5;

    public int BatchSize { get; set; } = 10;

    public long MaxPackageExpandedBytes { get; set; } = 100 * 1024 * 1024;

    public int MaxExtractedTextChars { get; set; } = 5_000_000;

    public int MaxPdfPages { get; set; } = 2_000;

    public int MaxXlsxCells { get; set; } = 250_000;
}

public sealed class TextExtractionWorkerOptionsValidator : IValidateOptions<TextExtractionWorkerOptions>
{
    public ValidateOptionsResult Validate(string? name, TextExtractionWorkerOptions options)
    {
        var failures = new List<string>();

        if (options.PollIntervalSeconds <= 0 || options.PollIntervalSeconds > 3600)
        {
            failures.Add("TextExtractionWorker:PollIntervalSeconds must be between 1 and 3600 seconds.");
        }

        if (options.BatchSize <= 0 || options.BatchSize > 500)
        {
            failures.Add("TextExtractionWorker:BatchSize must be between 1 and 500.");
        }

        if (options.MaxPackageExpandedBytes <= 0 || options.MaxPackageExpandedBytes > 1_073_741_824)
        {
            failures.Add("TextExtractionWorker:MaxPackageExpandedBytes must be between 1 byte and 1 GiB.");
        }

        if (options.MaxExtractedTextChars <= 0 || options.MaxExtractedTextChars > 50_000_000)
        {
            failures.Add("TextExtractionWorker:MaxExtractedTextChars must be between 1 and 50000000.");
        }

        if (options.MaxPdfPages <= 0 || options.MaxPdfPages > 100_000)
        {
            failures.Add("TextExtractionWorker:MaxPdfPages must be between 1 and 100000.");
        }

        if (options.MaxXlsxCells <= 0 || options.MaxXlsxCells > 5_000_000)
        {
            failures.Add("TextExtractionWorker:MaxXlsxCells must be between 1 and 5000000.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
