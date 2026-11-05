using RabbitMQ.Client;

namespace Inbox.Infrastructure.Messaging;

/// <summary>The broker topology of ADR-003 / contracts §4, declared idempotently by both hosts at startup.</summary>
public static class RabbitMqTopology
{
    public const string IncomingExchange = "inbox.incoming";
    public const string IncomingRoutingKey = "telegram.text";
    public const string IncomingQueue = "inbox.incoming.telegram";

    public const string OutgoingExchange = "inbox.outgoing";
    public const string OutgoingRoutingKey = "telegram.send";
    public const string OutgoingQueue = "inbox.outgoing.telegram";

    public const string DeadLetterExchange = "inbox.dlx";
    public const string IncomingDeadLetterKey = "incoming";
    public const string OutgoingDeadLetterKey = "outgoing";
    public const string IncomingDeadLetterQueue = "inbox.incoming.telegram.dlq";
    public const string OutgoingDeadLetterQueue = "inbox.outgoing.telegram.dlq";

    public const string EventsExchange = "inbox.events";

    public const int DeliveryLimit = 5;

    public static async Task DeclareAsync(IChannel channel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);

        await channel.ExchangeDeclareAsync(IncomingExchange, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(OutgoingExchange, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(EventsExchange, ExchangeType.Fanout, durable: true, autoDelete: false, cancellationToken: cancellationToken);

        await DeclareQuorumQueueAsync(channel, IncomingDeadLetterQueue, arguments: null, cancellationToken);
        await DeclareQuorumQueueAsync(channel, OutgoingDeadLetterQueue, arguments: null, cancellationToken);
        await channel.QueueBindAsync(IncomingDeadLetterQueue, DeadLetterExchange, IncomingDeadLetterKey, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(OutgoingDeadLetterQueue, DeadLetterExchange, OutgoingDeadLetterKey, cancellationToken: cancellationToken);

        await DeclareQuorumQueueAsync(channel, IncomingQueue, WorkQueueArguments(IncomingDeadLetterKey), cancellationToken);
        await DeclareQuorumQueueAsync(channel, OutgoingQueue, WorkQueueArguments(OutgoingDeadLetterKey), cancellationToken);
        await channel.QueueBindAsync(IncomingQueue, IncomingExchange, IncomingRoutingKey, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(OutgoingQueue, OutgoingExchange, OutgoingRoutingKey, cancellationToken: cancellationToken);
    }

    private static Dictionary<string, object?> WorkQueueArguments(string deadLetterRoutingKey) => new()
    {
        ["x-delivery-limit"] = DeliveryLimit,
        ["x-dead-letter-exchange"] = DeadLetterExchange,
        ["x-dead-letter-routing-key"] = deadLetterRoutingKey,
    };

    private static Task<QueueDeclareOk> DeclareQuorumQueueAsync(IChannel channel, string queue, Dictionary<string, object?>? arguments, CancellationToken cancellationToken)
    {
        var all = new Dictionary<string, object?>(arguments ?? []) { ["x-queue-type"] = "quorum" };
        return channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, arguments: all, cancellationToken: cancellationToken);
    }
}
