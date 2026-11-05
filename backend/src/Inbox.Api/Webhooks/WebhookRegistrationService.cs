using Inbox.Application.UseCases;

namespace Inbox.Api.Webhooks;

/// <summary>
/// Registers the Telegram webhook at the ngrok URL without blocking startup (D-041), retrying with backoff, then
/// re-checks every watchdog interval and re-registers if the URL changed (architecture §5).
/// </summary>
internal sealed partial class WebhookRegistrationService(
    IServiceScopeFactory scopes,
    WebhookWatchdogSettings watchdog,
    ILogger<WebhookRegistrationService> logger) : BackgroundService
{
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var force = true;
        var retryDelay = InitialRetryDelay;
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan wait;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<RegisterWebhookHandler>();
                var outcome = await handler.HandleAsync(force, stoppingToken);
                if (outcome == WebhookRegistrationOutcome.TunnelNotReady)
                {
                    LogTunnelNotReady(logger, retryDelay.TotalSeconds);
                    wait = NextRetry(ref retryDelay);
                }
                else
                {
                    force = false;
                    retryDelay = InitialRetryDelay;
                    wait = watchdog.Interval;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                LogFailed(logger, ex.GetType().Name, ex.Message, retryDelay.TotalSeconds);
                wait = NextRetry(ref retryDelay);
            }

            try
            {
                await Task.Delay(wait, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private static TimeSpan NextRetry(ref TimeSpan retryDelay)
    {
        var current = retryDelay;
        retryDelay = TimeSpan.FromTicks(Math.Min(retryDelay.Ticks * 2, MaxRetryDelay.Ticks));
        return current;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "No public ngrok https tunnel yet; retrying webhook registration in {DelaySeconds}s")]
    private static partial void LogTunnelNotReady(ILogger logger, double delaySeconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "Webhook registration failed ({ErrorType}: {Error}); retrying in {DelaySeconds}s")]
    private static partial void LogFailed(ILogger logger, string errorType, string error, double delaySeconds);
}

internal sealed record WebhookWatchdogSettings(TimeSpan Interval);
