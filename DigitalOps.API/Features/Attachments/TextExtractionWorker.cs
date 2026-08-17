using Microsoft.Extensions.Options;

namespace DigitalOps.API.Features.Attachments;

public sealed class TextExtractionWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<TextExtractionWorkerOptions> options,
    ILogger<TextExtractionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Text extraction worker is disabled by configuration.");
            return;
        }

        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(options.Value.PollIntervalSeconds));

        await RunOnceAsync(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunOnceAsync(stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<IAttachmentExtractionProcessor>();
            var result = await processor.ProcessPendingAsync(
                options.Value.BatchSize,
                cancellationToken);

            if (result.ProcessedCount > 0)
            {
                logger.LogInformation(
                    "Text extraction worker processed {ProcessedCount} attachments: {SucceededCount} succeeded, {FailedCount} failed, {UnsupportedCount} unsupported.",
                    result.ProcessedCount,
                    result.SucceededCount,
                    result.FailedCount,
                    result.UnsupportedCount);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during application shutdown.
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Text extraction worker cycle failed; the next cycle will retry.");
        }
    }
}
