using System.Text.Json;
using Inbox.Application.Abstractions;
using Inbox.Application.Contracts;
using Inbox.Infrastructure.Serialization;

namespace Inbox.Infrastructure.Messaging;

/// <summary>Worker-side notifier: publishes events to the <c>inbox.events</c> fanout; the api relays them to SignalR.</summary>
internal sealed class RabbitMqInboxNotifier(RabbitMqPublisher publisher) : IInboxNotifier
{
    public Task MessageReceivedAsync(MessageEventPayload payload, CancellationToken cancellationToken) =>
        PublishAsync(InboxEventNames.MessageReceived, payload, cancellationToken);

    public Task MessageQueuedAsync(MessageEventPayload payload, CancellationToken cancellationToken) =>
        PublishAsync(InboxEventNames.MessageQueued, payload, cancellationToken);

    public Task MessageStatusChangedAsync(MessageEventPayload payload, CancellationToken cancellationToken) =>
        PublishAsync(InboxEventNames.MessageStatusChanged, payload, cancellationToken);

    public Task ConversationReadAsync(ConversationReadPayload payload, CancellationToken cancellationToken) =>
        PublishAsync(InboxEventNames.ConversationRead, payload, cancellationToken);

    private Task PublishAsync<T>(string eventType, T payload, CancellationToken cancellationToken)
    {
        var envelope = new InboxEvent(eventType, JsonSerializer.SerializeToElement(payload, InboxJson.Options));
        return publisher.PublishEventAsync(RabbitMqTopology.EventsExchange, InboxEvent.TypeName, envelope, cancellationToken);
    }
}
