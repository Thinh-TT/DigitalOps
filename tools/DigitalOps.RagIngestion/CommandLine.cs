namespace DigitalOps.RagIngestion;

internal enum RagIngestionOperation
{
    Validate,
    Plan,
    Admit,
    Publish
}

internal sealed record RagIngestionOptions(
    RagIngestionOperation Operation,
    string StagingDirectory,
    bool Resume,
    bool UsesLegacySyntax,
    string? SourceRegistryPath = null,
    string? ApprovedBy = null,
    string? ApprovalReference = null);

internal sealed record CommandLineParseResult(
    RagIngestionOptions? Options,
    string? Error,
    bool ShowHelp = false,
    bool ShowVersion = false)
{
    public bool IsSuccess => Options is not null || ShowHelp || ShowVersion;
}

internal static class CommandLine
{
    public const string Usage =
        "Usage:\n"
        + "  DigitalOps.RagIngestion validate --staging-dir <path>\n"
        + "  DigitalOps.RagIngestion plan    --staging-dir <path>\n"
        + "  DigitalOps.RagIngestion admit   --staging-dir <path> --source-registry <path> --approved-by <name> --approval-reference <id>\n"
        + "  DigitalOps.RagIngestion publish --staging-dir <path> --source-registry <path> [--resume]\n\n"
        + "Commands:\n"
        + "  validate  Validate package integrity; no network or writes.\n"
        + "  plan      Validate and compute deterministic IDs; no network or writes.\n"
        + "  admit     Evaluate source/legal metadata and write a digest-bound admission receipt.\n"
        + "  publish   Write an admitted package to PostgreSQL and Qdrant.\n\n"
        + "Compatibility aliases (deprecated): --validate-only, --dry-run, "
        + "or no command for publish.";

    public static CommandLineParseResult Parse(IReadOnlyList<string> args)
    {
        RagIngestionOperation? operation = null;
        string? stagingDirectory = null;
        string? sourceRegistryPath = null;
        string? approvedBy = null;
        string? approvalReference = null;
        var resume = false;
        var validateOnly = false;
        var dryRun = false;
        var explicitCommand = false;

        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "validate":
                case "plan":
                case "admit":
                case "publish":
                    if (explicitCommand)
                    {
                        return Failure("Only one command may be specified.");
                    }

                    explicitCommand = true;
                    operation = argument switch
                    {
                        "validate" => RagIngestionOperation.Validate,
                        "plan" => RagIngestionOperation.Plan,
                        "admit" => RagIngestionOperation.Admit,
                        _ => RagIngestionOperation.Publish
                    };
                    break;

                case "--staging-dir":
                    if (index + 1 >= args.Count
                        || args[index + 1].StartsWith(
                            "--",
                            StringComparison.Ordinal))
                    {
                        return Failure("--staging-dir requires a path.");
                    }

                    if (stagingDirectory is not null)
                    {
                        return Failure("--staging-dir may only be specified once.");
                    }

                    stagingDirectory = args[++index];
                    break;

                case "--source-registry":
                    if (!TryReadValue(args, ref index, out sourceRegistryPath))
                    {
                        return Failure("--source-registry requires a path.");
                    }
                    break;

                case "--approved-by":
                    if (!TryReadValue(args, ref index, out approvedBy))
                    {
                        return Failure("--approved-by requires a value.");
                    }
                    break;

                case "--approval-reference":
                    if (!TryReadValue(args, ref index, out approvalReference))
                    {
                        return Failure("--approval-reference requires a value.");
                    }
                    break;

                case "--resume":
                    resume = true;
                    break;

                case "--validate-only":
                    validateOnly = true;
                    break;

                case "--dry-run":
                    dryRun = true;
                    break;

                case "--help":
                case "-h":
                    return new CommandLineParseResult(null, null, ShowHelp: true);

                case "--version":
                    return new CommandLineParseResult(null, null, ShowVersion: true);

                default:
                    return Failure($"Unknown argument '{argument}'.");
            }
        }

        if (validateOnly && dryRun)
        {
            return Failure("--validate-only and --dry-run cannot be combined.");
        }

        var legacyOperation = validateOnly
            ? RagIngestionOperation.Validate
            : dryRun
                ? RagIngestionOperation.Plan
                : RagIngestionOperation.Publish;

        if (explicitCommand
            && (validateOnly || dryRun)
            && operation != legacyOperation)
        {
            return Failure("The command conflicts with its compatibility flag.");
        }

        operation ??= legacyOperation;

        if (resume && operation != RagIngestionOperation.Publish)
        {
            return Failure("--resume is only valid with the publish command.");
        }

        if (operation is RagIngestionOperation.Admit or RagIngestionOperation.Publish
            && string.IsNullOrWhiteSpace(sourceRegistryPath))
        {
            return Failure(
                "--source-registry is required for admit and publish.");
        }
        if (operation == RagIngestionOperation.Admit
            && (string.IsNullOrWhiteSpace(approvedBy)
                || string.IsNullOrWhiteSpace(approvalReference)))
        {
            return Failure(
                "admit requires --approved-by and --approval-reference.");
        }
        if (operation != RagIngestionOperation.Admit
            && (approvedBy is not null || approvalReference is not null))
        {
            return Failure(
                "--approved-by and --approval-reference are only valid with admit.");
        }

        if (string.IsNullOrWhiteSpace(stagingDirectory))
        {
            return Failure("--staging-dir is required.");
        }

        return new CommandLineParseResult(
            new RagIngestionOptions(
                operation.Value,
                stagingDirectory,
                resume,
                UsesLegacySyntax: !explicitCommand || validateOnly || dryRun,
                sourceRegistryPath,
                approvedBy,
                approvalReference),
            null);
    }

    private static bool TryReadValue(
        IReadOnlyList<string> args,
        ref int index,
        out string? value)
    {
        value = null;
        if (index + 1 >= args.Count
            || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            return false;
        }
        value = args[++index];
        return true;
    }

    private static CommandLineParseResult Failure(string message) =>
        new(null, message);
}
