using Inbox.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Inbox.Application.UseCases;

/// <summary>
/// Outbox sweeper: re-publishes Pending replies whose last hand-off to the queue is older than <c>staleAfter</c>.
/// Closes the "committed but never published" gap; re-publishing is safe because delivery is idempotent (D-029).
/// </summary>
public sealed partial class RequeueStalePendingRepliesHandler(
    IMessageRepository messages,
    IUnitOfWork unitOfWork,
    IOutgoingMessagePublisher publisher,
    TimeProvider clock,
    ILogger<RequeueStalePendingRepliesHandler> logger)
{
    public async Task<int> HandleAsync(TimeSpan staleAfter, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var stale = await messages.ListPendingQueuedBeforeAsync(now - staleAfter, cancellationToken);
        var requeued = 0;
        foreach (var message in stale)
        {
            // Stop at the first failure: the broker or the database is down, and the next sweep starts over.
            if (!await OutboundPublishing.TryPublishAsync(publisher, message.Id, logger, cancellationToken))
            {
                break;
            }

            // Saved per message right after its publish, so a later failure never re-publishes it on the next sweep (D-077).
            message.RecordRequeued(now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            requeued++;
        }

        if (requeued > 0)
        {
            LogRequeued(logger, requeued);
        }

        return requeued;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox sweeper re-published {Count} stale Pending replies")]
    private static partial void LogRequeued(ILogger logger, int count);
}
