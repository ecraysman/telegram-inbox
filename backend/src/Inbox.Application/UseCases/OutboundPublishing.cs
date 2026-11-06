using Inbox.Application.Abstractions;
using Inbox.Application.Contracts;
using Microsoft.Extensions.Logging;

namespace Inbox.Application.UseCases;

internal static partial class OutboundPublishing
{
    /// <summary>Publishes a reply; on failure the row stays Pending for the sweeper, so the caller still succeeds.</summary>
    public static async Task<bool> TryPublishAsync(IOutgoingMessagePublisher publisher, Guid messageId, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            await publisher.PublishAsync(new OutgoingTextMessage(messageId), cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogPublishFailed(logger, ex, messageId);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not publish reply {MessageId}; it stays Pending and the outbox sweeper will publish it")]
    private static partial void LogPublishFailed(ILogger logger, Exception exception, Guid messageId);
}
