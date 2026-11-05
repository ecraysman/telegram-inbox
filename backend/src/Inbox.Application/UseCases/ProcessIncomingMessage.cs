using Inbox.Application.Abstractions;
using Inbox.Application.Contracts;
using Inbox.Application.Errors;
using Inbox.Domain.Common;
using Inbox.Domain.Contacts;
using Inbox.Domain.Messages;
using Microsoft.Extensions.Logging;

namespace Inbox.Application.UseCases;

/// <summary>Worker: persists an inbound message idempotently, then notifies (contracts §7.4).</summary>
public sealed partial class ProcessIncomingMessageHandler(
    IContactRepository contacts,
    IMessageRepository messages,
    IUnitOfWork unitOfWork,
    EventPayloads payloads,
    IInboxNotifier notifier,
    TimeProvider clock,
    ILogger<ProcessIncomingMessageHandler> logger)
{
    public async Task HandleAsync(IncomingTextMessage incoming, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        if (incoming.From is null)
        {
            throw new PoisonMessageException($"Update {incoming.UpdateId} has no sender.");
        }

        var now = clock.GetUtcNow();
        MessageText text;
        try
        {
            text = MessageText.Create(incoming.Text);
        }
        catch (DomainException ex)
        {
            throw new PoisonMessageException($"Update {incoming.UpdateId} has invalid text: {ex.Message}");
        }

        Guid messageId;
        Guid contactId;
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var contact = await contacts.GetByTelegramChatIdAsync(incoming.ChatId, cancellationToken);
            if (contact is null)
            {
                contact = Contact.FromTelegram(incoming.ChatId, incoming.From.FirstName, incoming.From.LastName, incoming.From.Username, now);
                contacts.Add(contact);
            }
            else
            {
                contact.UpdateProfile(incoming.From.FirstName, incoming.From.LastName, incoming.From.Username, now);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            contactId = contact.Id;

            var message = Message.Inbound(contact.Id, incoming.UpdateId, incoming.TelegramMessageId, text, incoming.SentAt.ToUniversalTime());
            if (await messages.TryInsertInboundAsync(message, cancellationToken))
            {
                messageId = message.Id;
            }
            else
            {
                var existing = await messages.GetByTelegramUpdateIdAsync(incoming.UpdateId, cancellationToken)
                    ?? throw new InvalidOperationException($"Update {incoming.UpdateId} conflicted but no row was found.");
                messageId = existing.Id;
                LogDuplicate(logger, incoming.UpdateId, messageId);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        var payload = await payloads.ForMessageAsync(messageId, contactId, cancellationToken);
        await notifier.MessageReceivedAsync(payload, cancellationToken);
        LogPersisted(logger, incoming.UpdateId, messageId, contactId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Update {UpdateId} was already stored as message {MessageId}; re-notifying")]
    private static partial void LogDuplicate(ILogger logger, long updateId, Guid messageId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Inbound update {UpdateId} stored as message {MessageId} for contact {ContactId}")]
    private static partial void LogPersisted(ILogger logger, long updateId, Guid messageId, Guid contactId);
}
