using Inbox.Application.Contracts;

namespace Inbox.Application.Abstractions;

/// <summary>Publishes to the inbound work queue. Completes only after the broker confirmed; throws otherwise.</summary>
public interface IIncomingMessagePublisher
{
    Task PublishAsync(IncomingTextMessage message, CancellationToken cancellationToken);
}

/// <summary>Publishes to the outbound work queue. Completes only after the broker confirmed; throws otherwise.</summary>
public interface IOutgoingMessagePublisher
{
    Task PublishAsync(OutgoingTextMessage message, CancellationToken cancellationToken);
}

/// <summary>Pushes real-time events to operators (SignalR in the api, the inbox.events exchange in the worker).</summary>
public interface IInboxNotifier
{
    Task MessageReceivedAsync(MessageEventPayload payload, CancellationToken cancellationToken);

    Task MessageQueuedAsync(MessageEventPayload payload, CancellationToken cancellationToken);

    Task MessageStatusChangedAsync(MessageEventPayload payload, CancellationToken cancellationToken);

    Task ConversationReadAsync(ConversationReadPayload payload, CancellationToken cancellationToken);
}
