using System.Text.Json;
using Inbox.Infrastructure.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Inbox.Infrastructure.Messaging;

/// <summary>Receives events relayed from <c>inbox.events</c> (implemented by the api's SignalR relay).</summary>
public interface IInboxEventSink
{
    Task ForwardAsync(string eventType, JsonElement payload, CancellationToken cancellationToken);
}

/// <summary>
/// Binds an exclusive, auto-delete, server-named queue to the <c>inbox.events</c> fanout and forwards every event
/// to the <see cref="IInboxEventSink"/>. Events are transient: losing one is covered by the client's refetch on reconnect.
/// </summary>
public sealed partial class InboxEventsSubscriber(
    RabbitMqConnection connection,
    IInboxEventSink sink,
    ILogger<InboxEventsSubscriber> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await connection.EnsureConnectedAsync(stoppingToken);
        var amqp = await connection.GetAsync(stoppingToken);
        await using var channel = await amqp.CreateChannelAsync(cancellationToken: stoppingToken);
        var queue = await channel.QueueDeclareAsync(queue: string.Empty, durable: false, exclusive: true, autoDelete: true, cancellationToken: stoppingToken);
        await channel.QueueBindAsync(queue.QueueName, RabbitMqTopology.EventsExchange, routingKey: string.Empty, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => ForwardAsync(delivery, stoppingToken);
        await channel.BasicConsumeAsync(queue.QueueName, autoAck: true, consumer, stoppingToken);
        LogSubscribed(logger, queue.QueueName);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Host shutdown.
        }
    }

    private async Task ForwardAsync(BasicDeliverEventArgs delivery, CancellationToken cancellationToken)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<InboxEvent>(delivery.Body.Span, InboxJson.Options);
            if (envelope is null || string.IsNullOrEmpty(envelope.EventType))
            {
                LogDropped(logger, null);
                return;
            }

            await sink.ForwardAsync(envelope.EventType, envelope.Payload, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogDropped(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Relaying inbox.events through queue {Queue}")]
    private static partial void LogSubscribed(ILogger logger, string queue);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dropped an inbox event that could not be relayed")]
    private static partial void LogDropped(ILogger logger, Exception? exception);
}
