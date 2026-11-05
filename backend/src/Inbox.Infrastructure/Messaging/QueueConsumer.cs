using System.Text.Json;
using Inbox.Application.Errors;
using Inbox.Domain.Common;
using Inbox.Infrastructure.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Inbox.Infrastructure.Messaging;

/// <summary>
/// Base consumer for a work queue: prefetch 1, manual ack after the handler (which commits) succeeds.
/// Permanent errors (undeserializable payload, <see cref="PoisonMessageException"/>, <see cref="DomainException"/>)
/// are nacked without requeue, so they go to the DLQ. Transient errors are retried in process after 1, 2 and 4 s,
/// then nacked with requeue; the quorum delivery limit dead-letters them eventually (ADR-003).
/// </summary>
public abstract partial class QueueConsumer<TMessage>(
    RabbitMqConnection connection,
    IServiceScopeFactory scopes,
    ILogger logger) : BackgroundService
    where TMessage : class
{
    private const int MaxRetries = 3;

    protected abstract string QueueName { get; }

    protected abstract Task HandleAsync(IServiceProvider services, TMessage message, CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await connection.EnsureConnectedAsync(stoppingToken);
        var amqp = await connection.GetAsync(stoppingToken);
        await using var channel = await amqp.CreateChannelAsync(cancellationToken: stoppingToken);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => OnReceivedAsync(channel, delivery, stoppingToken);
        await channel.BasicConsumeAsync(QueueName, autoAck: false, consumer, stoppingToken);
        LogConsuming(logger, QueueName);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Host shutdown: unacked deliveries are requeued by the broker when the channel closes.
        }
    }

    private async Task OnReceivedAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        var tag = delivery.DeliveryTag;
        TMessage message;
        try
        {
            message = JsonSerializer.Deserialize<TMessage>(delivery.Body.Span, InboxJson.Options)
                ?? throw new JsonException("The payload is null.");
        }
        catch (JsonException ex)
        {
            LogPoison(logger, ex, QueueName, delivery.BasicProperties.MessageId);
            await channel.BasicNackAsync(tag, multiple: false, requeue: false, CancellationToken.None);
            return;
        }

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using (var scope = scopes.CreateAsyncScope())
                {
                    await HandleAsync(scope.ServiceProvider, message, stoppingToken);
                }

                await channel.BasicAckAsync(tag, multiple: false, CancellationToken.None);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is PoisonMessageException or DomainException)
            {
                LogPoison(logger, ex, QueueName, delivery.BasicProperties.MessageId);
                await channel.BasicNackAsync(tag, multiple: false, requeue: false, CancellationToken.None);
                return;
            }
            catch (Exception ex) when (attempt < MaxRetries)
            {
                var delay = TimeSpan.FromSeconds(1 << attempt);
                LogTransient(logger, ex, QueueName, delivery.BasicProperties.MessageId, attempt + 1, delay.TotalSeconds);
                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                LogRequeue(logger, ex, QueueName, delivery.BasicProperties.MessageId);
                await channel.BasicNackAsync(tag, multiple: false, requeue: true, CancellationToken.None);
                return;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Consuming {Queue} (prefetch 1, manual ack)")]
    private static partial void LogConsuming(ILogger logger, string queue);

    [LoggerMessage(Level = LogLevel.Error, Message = "Poison message {MessageId} on {Queue}; dead-lettering it")]
    private static partial void LogPoison(ILogger logger, Exception exception, string queue, string? messageId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Transient failure on {Queue} for {MessageId} (retry {Retry} in {DelaySeconds}s)")]
    private static partial void LogTransient(ILogger logger, Exception exception, string queue, string? messageId, int retry, double delaySeconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "Still failing on {Queue} for {MessageId} after retries; requeueing")]
    private static partial void LogRequeue(ILogger logger, Exception exception, string queue, string? messageId);
}
