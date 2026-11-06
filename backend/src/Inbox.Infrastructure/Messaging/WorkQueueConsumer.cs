using RabbitMQ.Client;

namespace Inbox.Infrastructure.Messaging;

/// <summary>One delivery handed to <see cref="QueueConsumer{TMessage}"/>. <see cref="Body"/> is valid until the callback completes.</summary>
internal sealed record WorkQueueDelivery(ulong Tag, string? MessageId, ReadOnlyMemory<byte> Body);

/// <summary>
/// Minimal consumer that tells a server-sent basic.cancel apart from an unregistration caused by a channel or
/// connection shutdown: the client calls <see cref="HandleBasicCancelAsync"/> only for the broker's basic.cancel frame
/// (D-094). Shutdowns are handled from the channel's own ShutdownEventArgs.
/// </summary>
internal sealed class WorkQueueConsumer(IChannel channel, Func<WorkQueueDelivery, Task> onDelivery, Action onServerCancel)
    : AsyncDefaultBasicConsumer(channel)
{
    public override Task HandleBasicDeliverAsync(
        string consumerTag,
        ulong deliveryTag,
        bool redelivered,
        string exchange,
        string routingKey,
        IReadOnlyBasicProperties properties,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken = default) =>
        onDelivery(new WorkQueueDelivery(deliveryTag, properties.MessageId, body));

    public override async Task HandleBasicCancelAsync(string consumerTag, CancellationToken cancellationToken = default)
    {
        onServerCancel();
        await base.HandleBasicCancelAsync(consumerTag, cancellationToken);
    }
}
