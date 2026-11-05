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
            if (!await OutboundPublishing.TryPublishAsync(publisher, message.Id, logger, cancellationToken))
            {
                break;
            }

            message.RecordRequeued(now);
            requeued++;
        }

        if (requeued > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            LogRequeued(logger, requeued);
        }

        return requeued;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox sweeper re-published {Count} stale Pending replies")]
    private static partial void LogRequeued(ILogger logger, int count);
}
