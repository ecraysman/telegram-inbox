using Inbox.Application.Abstractions;
using Inbox.Application.Contracts;
using Inbox.Application.Errors;
using Inbox.Domain.Messages;
using Microsoft.Extensions.Logging;

namespace Inbox.Application.UseCases;

/// <summary>Operator retry of a Failed reply: Failed → Pending, push messageStatusChanged, re-publish (D-032).</summary>
public sealed class RetryReplyHandler(
    IMessageRepository messages,
    IUnitOfWork unitOfWork,
    EventPayloads payloads,
    IInboxNotifier notifier,
    IOutgoingMessagePublisher publisher,
    TimeProvider clock,
    ILogger<RetryReplyHandler> logger)
{
    public async Task<MessageDto> HandleAsync(Guid messageId, CancellationToken cancellationToken)
    {
        var message = await messages.GetByIdAsync(messageId, cancellationToken)
            ?? throw new MessageNotFoundException(messageId);
        if (message.Status != DeliveryStatus.Failed)
        {
            throw new MessageNotFailedException(messageId);
        }

        message.RequeueForRetry(clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var payload = await payloads.ForMessageAsync(message.Id, message.ContactId, cancellationToken);
        await notifier.MessageStatusChangedAsync(payload, cancellationToken);
        await OutboundPublishing.TryPublishAsync(publisher, message.Id, logger, cancellationToken);
        return payload.Message;
    }
}
