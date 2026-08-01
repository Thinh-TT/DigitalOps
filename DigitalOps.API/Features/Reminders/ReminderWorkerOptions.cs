using Microsoft.Extensions.Options;

namespace DigitalOps.API.Features.Reminders;

public sealed class ReminderWorkerOptions
{
    public const string SectionName = "ReminderWorker";

    public bool Enabled { get; set; } = true;

    public int RunIntervalMinutes { get; set; } = 15;

    public int BeforeDeadlineDays { get; set; } = 3;

    public string TimeZoneId { get; set; } = "Asia/Ho_Chi_Minh";
}

public sealed class ReminderWorkerOptionsValidator : IValidateOptions<ReminderWorkerOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        ReminderWorkerOptions options)
    {
        var failures = new List<string>();

        if (options.RunIntervalMinutes is < 1 or > 1440)
        {
            failures.Add("ReminderWorker:RunIntervalMinutes must be between 1 and 1440.");
        }

        if (options.BeforeDeadlineDays is < 1 or > 365)
        {
            failures.Add("ReminderWorker:BeforeDeadlineDays must be between 1 and 365.");
        }

        if (string.IsNullOrWhiteSpace(options.TimeZoneId)
            || !ReminderTimeZoneResolver.TryResolve(options.TimeZoneId, out _))
        {
            failures.Add("ReminderWorker:TimeZoneId must be a valid IANA or Windows timezone ID.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

public static class ReminderTimeZoneResolver
{
    public static bool TryResolve(string id, out TimeZoneInfo? timeZone)
    {
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            // Try the platform-specific equivalent below.
        }
        catch (InvalidTimeZoneException)
        {
            timeZone = null;
            return false;
        }

        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windowsId))
        {
            try
            {
                timeZone = TimeZoneInfo.FindSystemTimeZoneById(windowsId);
                return true;
            }
            catch (TimeZoneNotFoundException)
            {
                // Fall through to the reverse conversion.
            }
            catch (InvalidTimeZoneException)
            {
                timeZone = null;
                return false;
            }
        }

        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var ianaId))
        {
            try
            {
                timeZone = TimeZoneInfo.FindSystemTimeZoneById(ianaId);
                return true;
            }
            catch (TimeZoneNotFoundException)
            {
                // The runtime has no timezone data for the configured ID.
            }
            catch (InvalidTimeZoneException)
            {
                timeZone = null;
                return false;
            }
        }

        timeZone = null;
        return false;
    }
}
