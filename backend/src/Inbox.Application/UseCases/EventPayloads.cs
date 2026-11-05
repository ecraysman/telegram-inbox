using Inbox.Application.Abstractions;
using Inbox.Application.Contracts;

namespace Inbox.Application.UseCases;

/// <summary>Builds full event payloads from the read side, so every event carries the same shape as the REST API.</summary>
public sealed class EventPayloads(IInboxQueries queries)
{
    public async Task<MessageEventPayload> ForMessageAsync(Guid messageId, Guid contactId, CancellationToken cancellationToken)
    {
        var message = await queries.GetMessageAsync(messageId, cancellationToken)
            ?? throw new InvalidOperationException($"Message {messageId} vanished while building its event.");
        var contact = await ForContactAsync(contactId, cancellationToken);
        return new MessageEventPayload(message, contact);
    }

    public async Task<ContactSummaryDto> ForContactAsync(Guid contactId, CancellationToken cancellationToken) =>
        await queries.GetContactSummaryAsync(contactId, cancellationToken)
            ?? throw new InvalidOperationException($"Contact {contactId} vanished while building its event.");
}
