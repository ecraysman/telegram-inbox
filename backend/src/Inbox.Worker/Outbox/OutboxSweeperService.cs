using Inbox.Application.UseCases;

namespace Inbox.Worker.Outbox;

internal sealed record OutboxSettings(TimeSpan SweepInterval, TimeSpan StaleAfter);

/// <summary>Periodically re-publishes stale Pending replies (D-029, ADR-004).</summary>
internal sealed partial class OutboxSweeperService(
    IServiceScopeFactory scopes,
    OutboxSettings settings,
    TimeProvider clock,
    ILogger<OutboxSweeperService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(settings.SweepInterval, clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<RequeueStalePendingRepliesHandler>()
                        .HandleAsync(settings.StaleAfter, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogSweepFailed(logger, ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Host shutdown.
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox sweep failed; it runs again at the next interval")]
    private static partial void LogSweepFailed(ILogger logger, Exception exception);
}
