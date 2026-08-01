using Microsoft.Extensions.Options;

namespace DigitalOps.API.Features.Reminders;

public sealed class ReminderWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ReminderWorkerOptions> options,
    TimeProvider timeProvider,
    ILogger<ReminderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Reminder worker is disabled by configuration.");
            return;
        }

        if (!ReminderTimeZoneResolver.TryResolve(options.Value.TimeZoneId, out var timeZone))
        {
            throw new InvalidOperationException(
                $"ReminderWorker timezone '{options.Value.TimeZoneId}' could not be resolved.");
        }

        using var timer = new PeriodicTimer(
            TimeSpan.FromMinutes(options.Value.RunIntervalMinutes));
        await RunOnceAsync(timeZone!, stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunOnceAsync(timeZone!, stoppingToken);
        }
    }

    private async Task RunOnceAsync(
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        try
        {
            var utcNow = timeProvider.GetUtcNow();
            var businessDate = DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTime(utcNow, timeZone).DateTime);

            await using var scope = scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IReminderService>();
            var result = await service.ProcessAsync(businessDate, cancellationToken);

            logger.LogInformation(
                "Reminder worker cycle completed for {BusinessDate}: {OverdueDocuments} overdue documents, {CreatedReminders} reminders created, {ExistingReminders} reminders already present.",
                businessDate,
                result.OverdueDocuments,
                result.CreatedReminders,
                result.ExistingReminders);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host shutdown is expected and should not be logged as a failed cycle.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Reminder worker cycle failed; the next cycle will retry.");
        }
    }
}
