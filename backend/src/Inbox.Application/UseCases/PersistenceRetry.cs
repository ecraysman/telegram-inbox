using Inbox.Application.Errors;
using Microsoft.Extensions.Logging;

namespace Inbox.Application.UseCases;

/// <summary>
/// Persists the outcome of an irreversible side effect (a Telegram send). The save is retried without limit for any
/// error (1, 2, 4… capped at 30 s), is never dead-lettered and never leads to a second send within the delivery
/// (D-077, D-087). Only cancellation ends the loop: host shutdown, or the broker dropping the delivery's subscription.
/// A persistent non-transient failure therefore blocks the outbound queue until the database is fixed; that is the
/// accepted trade-off (inbound has its own consumer and keeps working).
/// </summary>
internal static partial class PersistenceRetry
{
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);

    public static async Task RunAsync(Func<CancellationToken, Task> step, TimeProvider clock, ILogger logger, Guid messageId, CancellationToken cancellationToken)
    {
        for (var failures = 0; ; failures++)
        {
            try
            {
                await step(cancellationToken);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                var delay = failures >= 5 ? MaxDelay : TimeSpan.FromSeconds(Math.Min(1 << failures, MaxDelay.TotalSeconds));
                LogRetry(logger, ex, messageId, failures + 1, ex is TransientPersistenceException, delay.TotalSeconds);
                await Task.Delay(delay, clock, cancellationToken);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "Could not persist the delivery outcome of reply {MessageId} (attempt {Attempt}, transient: {Transient}); retrying in {DelaySeconds}s without resending")]
    private static partial void LogRetry(ILogger logger, Exception exception, Guid messageId, int attempt, bool transient, double delaySeconds);
}
