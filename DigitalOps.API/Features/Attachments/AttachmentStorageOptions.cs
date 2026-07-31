using Microsoft.Extensions.Options;

namespace DigitalOps.API.Features.Attachments;

public sealed class AttachmentStorageOptions
{
    public const string SectionName = "AttachmentStorage";
    public const string DefaultRootPath = "App_Data/attachments";
    public const long DefaultMaxFileSizeBytes = 10 * 1024 * 1024;
    public const long MaximumConfigurableFileSizeBytes = 100 * 1024 * 1024;

    public string RootPath { get; set; } = DefaultRootPath;

    public long MaxFileSizeBytes { get; set; } = DefaultMaxFileSizeBytes;
}

public sealed class AttachmentStorageOptionsValidator(
    IWebHostEnvironment environment) : IValidateOptions<AttachmentStorageOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        AttachmentStorageOptions options)
    {
        var failures = new List<string>();

        if (options.MaxFileSizeBytes <= 0
            || options.MaxFileSizeBytes > AttachmentStorageOptions.MaximumConfigurableFileSizeBytes)
        {
            failures.Add(
                $"AttachmentStorage:MaxFileSizeBytes must be between 1 and {AttachmentStorageOptions.MaximumConfigurableFileSizeBytes} bytes.");
        }

        if (string.IsNullOrWhiteSpace(options.RootPath))
        {
            failures.Add("AttachmentStorage:RootPath is required.");
        }
        else
        {
            try
            {
                var rootPath = ResolvePath(environment.ContentRootPath, options.RootPath);
                var webRootPath = ResolvePath(
                    environment.ContentRootPath,
                    environment.WebRootPath ?? "wwwroot");

                if (IsSameOrChildPath(rootPath, webRootPath))
                {
                    failures.Add("AttachmentStorage:RootPath must be outside the web root.");
                }

                if (string.Equals(
                    rootPath,
                    Path.GetPathRoot(rootPath),
                    StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add("AttachmentStorage:RootPath cannot be a filesystem root.");
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
            {
                failures.Add("AttachmentStorage:RootPath is not a valid filesystem path.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    internal static string ResolvePath(string contentRootPath, string configuredPath) =>
        Path.GetFullPath(
            Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(contentRootPath, configuredPath));

    private static bool IsSameOrChildPath(string candidate, string parent)
    {
        var relative = Path.GetRelativePath(parent, candidate);
        return relative == "."
            || (!relative.StartsWith("..", StringComparison.Ordinal)
                && !Path.IsPathRooted(relative));
    }
}
