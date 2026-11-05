using Inbox.Application.Abstractions;
using Inbox.Application.Contracts;
using Inbox.Application.Errors;

namespace Inbox.Application.UseCases;

/// <summary>Marks unread inbound messages up to what the operator has seen (D-024). Emits conversationRead only if rows changed.</summary>
public sealed class MarkConversationAsReadHandler(
    IContactRepository contacts,
    IMessageRepository messages,
    IUnitOfWork unitOfWork,
    EventPayloads payloads,
    IInboxNotifier notifier,
    TimeProvider clock)
{
    public const string UpToMessageIdField = "upToMessageId";

    public async Task HandleAsync(Guid contactId, Guid upToMessageId, CancellationToken cancellationToken)
    {
        var contact = await contacts.GetByIdAsync(contactId, cancellationToken)
            ?? throw new ContactNotFoundException(contactId);
        var upTo = await messages.GetByIdAsync(upToMessageId, cancellationToken)
            ?? throw new MessageNotFoundException(upToMessageId);
        if (upTo.ContactId != contact.Id)
        {
            throw new ValidationFailedException(UpToMessageIdField, "The message belongs to another contact.");
        }

        var unread = await messages.ListUnreadAsync(contact.Id, cancellationToken);
        var changed = contact.MarkConversationReadUpTo(upTo, unread, clock.GetUtcNow());
        if (changed == 0)
        {
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        var summary = await payloads.ForContactAsync(contact.Id, cancellationToken);
        await notifier.ConversationReadAsync(new ConversationReadPayload(summary), cancellationToken);
    }
}
