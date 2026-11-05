using Inbox.Application.Abstractions;
using Inbox.Application.Contracts;
using Inbox.Application.Errors;
using Inbox.Domain.Common;
using Inbox.Domain.Messages;
using Microsoft.Extensions.Logging;

namespace Inbox.Application.UseCases;

/// <summary>
/// Stores the operator's reply as Pending (the row is the outbox), pushes messageQueued, then publishes it.
/// A failed publish still returns the Pending reply: the outbox sweeper publishes it later (D-029).
/// </summary>
public sealed partial class QueueReplyHandler(
    IContactRepository contacts,
    IMessageRepository messages,
    IUnitOfWork unitOfWork,
    EventPayloads payloads,
    IInboxNotifier notifier,
    IOutgoingMessagePublisher publisher,
    TimeProvider clock,
    ILogger<QueueReplyHandler> logger)
{
    public const string TextField = "text";

    public async Task<MessageDto> HandleAsync(Guid contactId, string? text, CancellationToken cancellationToken)
    {
        MessageText messageText;
        try
        {
            messageText = MessageText.Create(text);
        }
        catch (DomainException ex)
        {
            throw new ValidationFailedException(TextField, ex.Message);
        }

        var contact = await contacts.GetByIdAsync(contactId, cancellationToken)
            ?? throw new ContactNotFoundException(contactId);

        var message = Message.Outbound(contact.Id, messageText, clock.GetUtcNow());
        messages.Add(message);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var payload = await payloads.ForMessageAsync(message.Id, contact.Id, cancellationToken);
        await notifier.MessageQueuedAsync(payload, cancellationToken);
        await OutboundPublishing.TryPublishAsync(publisher, message.Id, logger, cancellationToken);
        return payload.Message;
    }
}

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
