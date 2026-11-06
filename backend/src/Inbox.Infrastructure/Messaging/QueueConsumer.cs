using System.Text.Json;
using Inbox.Infrastructure.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace Inbox.Infrastructure.Messaging;

/// <summary>
/// Base consumer for a work queue: prefetch 1, manual ack after the handler (which commits) succeeds.
/// Error classes per contracts §7.5 (<see cref="ConsumerErrorClassifier"/>): poison → nack without requeue (DLQ);
/// infrastructure → retried in process without limit (1, 2, 4… capped at 30 s); anything else → 3 retries (1, 2, 4 s),
/// then nack with requeue, and the quorum delivery limit eventually dead-letters it.
/// </summary>
public abstract partial class QueueConsumer<TMessage>(
    RabbitMqConnection connection,
    IServiceScopeFactory scopes,
    ILogger logger) : BackgroundService
    where TMessage : class
{
    private const int MaxUnexpectedRetries = 3;
    private const int MaxConsecutiveSetupFailures = 10;
    private static readonly TimeSpan ResubscribeDelay = TimeSpan.FromSeconds(2);

    private readonly SemaphoreSlim _processing = new(1, 1);

    protected abstract string QueueName { get; }

    protected abstract Task HandleAsync(IServiceProvider services, TMessage message, CancellationToken cancellationToken);

    /// <summary>
    /// Keeps exactly one live consumer. Automatic recovery restores the channel after a <em>connection</em> loss, but not
    /// when the broker drops the subscription on a live connection: a channel close (PRECONDITION_FAILED) or a
    /// server-sent basic.cancel (RabbitMQ 4 quorum queues apply consumer_timeout this way and return the message).
    /// Then the channel, topology and consumer are recreated. If that keeps failing, the exception stops the host so the
    /// container restart policy brings up a fresh process; a dead consumer is never left behind.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consecutiveFailures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            IChannel? channel = null;
            var session = new ConsumerSession(stoppingToken);
            try
            {
                await connection.EnsureConnectedAsync(stoppingToken);
                var amqp = await connection.GetAsync(stoppingToken);
                // Dispatch concurrency 3: prefetch 1 still delivers one message at a time per channel, but the broker's
                // basic.cancel must be dispatchable while a long handler runs and a redelivered copy waits for it.
                channel = await amqp.CreateChannelAsync(
                    new CreateChannelOptions(publisherConfirmationsEnabled: false, publisherConfirmationTrackingEnabled: false, consumerDispatchConcurrency: 3),
                    stoppingToken);

                channel.ChannelShutdownAsync += (_, args) =>
                {
                    if (args.Initiator != ShutdownInitiator.Application)
                    {
                        session.OnBrokerEvent(amqp.IsOpen, $"channel closed by the broker: {args.ReplyCode} {args.ReplyText}");
                    }

                    return Task.CompletedTask;
                };

                await RabbitMqTopology.DeclareAsync(channel, stoppingToken);
                await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, stoppingToken);
                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.UnregisteredAsync += (_, _) =>
                {
                    // This service never cancels its consumer itself, so this is the broker's basic.cancel.
                    session.OnBrokerEvent(amqp.IsOpen, "basic.cancel from the broker, e.g. consumer_timeout or a queue restart");
                    return Task.CompletedTask;
                };
                var subscribed = channel;
                consumer.ReceivedAsync += (_, delivery) => OnReceivedAsync(subscribed, session, delivery, stoppingToken);
                await channel.BasicConsumeAsync(QueueName, autoAck: false, consumer, stoppingToken);
                LogConsuming(logger, QueueName);
                consecutiveFailures = 0;

                var reason = await session.Lost.WaitAsync(stoppingToken);
                LogSubscriptionLost(logger, QueueName, reason);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (++consecutiveFailures < MaxConsecutiveSetupFailures)
            {
                LogSetupFailed(logger, ex, QueueName, consecutiveFailures);
            }
            finally
            {
                if (channel is not null)
                {
                    await DisposeQuietlyAsync(channel);
                }
            }

            try
            {
                await Task.Delay(ResubscribeDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task OnReceivedAsync(IChannel channel, ConsumerSession session, BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        var messageId = delivery.BasicProperties.MessageId;
        var target = new DeliveryTarget(channel, session, session.Generation, delivery.DeliveryTag);

        // Its token is cancelled only on shutdown or when the broker dropped the subscription on a live connection; then
        // the broker has requeued the message and any retry for it stops. A connection loss does not cancel it.
        var cancellationToken = session.DeliveryToken;
        try
        {
            // One handler at a time across sessions and recoveries: a redelivered copy waits for the original, so a
            // post-send save in progress is never raced by a second send (D-077).
            await _processing.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            await ProcessAsync(target, delivery, messageId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!stoppingToken.IsCancellationRequested)
            {
                LogAbandoned(logger, QueueName, messageId);
            }
        }
        finally
        {
            _processing.Release();
        }
    }

    private async Task ProcessAsync(DeliveryTarget target, BasicDeliverEventArgs delivery, string? messageId, CancellationToken cancellationToken)
    {
        TMessage message;
        try
        {
            message = JsonSerializer.Deserialize<TMessage>(delivery.Body.Span, InboxJson.Options)
                ?? throw new JsonException("The payload is null.");
        }
        catch (JsonException ex)
        {
            LogPoison(logger, ex, QueueName, messageId);
            await SettleAsync(target, ack: false, requeue: false);
            return;
        }

        var unexpectedFailures = 0;
        var infrastructureFailures = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TimeSpan delay;
            try
            {
                await using (var scope = scopes.CreateAsyncScope())
                {
                    await HandleAsync(scope.ServiceProvider, message, cancellationToken);
                }

                await SettleAsync(target, ack: true, requeue: false);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                switch (ConsumerErrorClassifier.Classify(ex))
                {
                    case ConsumerErrorClass.Poison:
                        LogPoison(logger, ex, QueueName, messageId);
                        await SettleAsync(target, ack: false, requeue: false);
                        return;

                    case ConsumerErrorClass.Infrastructure:
                        // No limit: an outage must never dead-letter a message Telegram already considers delivered (D-076).
                        delay = ConsumerErrorClassifier.InfrastructureBackoff(infrastructureFailures++);
                        LogInfrastructure(logger, ex, QueueName, messageId, infrastructureFailures, delay.TotalSeconds);
                        break;

                    default:
                        if (unexpectedFailures == MaxUnexpectedRetries)
                        {
                            LogRequeue(logger, ex, QueueName, messageId);
                            await SettleAsync(target, ack: false, requeue: true);
                            return;
                        }

                        delay = TimeSpan.FromSeconds(1 << unexpectedFailures++);
                        LogUnexpected(logger, ex, QueueName, messageId, unexpectedFailures, delay.TotalSeconds);
                        break;
                }
            }

            await Task.Delay(delay, cancellationToken);
        }
    }

    /// <summary>
    /// Acks or nacks only when the tag is still valid: same channel, open, and no connection loss since the delivery
    /// arrived. Otherwise the broker has already requeued the message, and a stale tag is skipped instead of being
    /// sent on the recovered channel.
    /// </summary>
    private async Task SettleAsync(DeliveryTarget target, bool ack, bool requeue)
    {
        if (!target.Channel.IsOpen || target.Session.Generation != target.Generation || target.Session.IsLost)
        {
            LogNotSettled(logger, QueueName, target.Tag);
            return;
        }

        try
        {
            if (ack)
            {
                await target.Channel.BasicAckAsync(target.Tag, multiple: false, CancellationToken.None);
            }
            else
            {
                await target.Channel.BasicNackAsync(target.Tag, multiple: false, requeue, CancellationToken.None);
            }
        }
        catch (AlreadyClosedException)
        {
            LogNotSettled(logger, QueueName, target.Tag);
        }
    }

    private static async Task DisposeQuietlyAsync(IChannel channel)
    {
        try
        {
            await channel.DisposeAsync();
        }
        catch (Exception)
        {
            // Best effort: the channel is already unusable.
        }
    }

    public override void Dispose()
    {
        _processing.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed record DeliveryTarget(IChannel Channel, ConsumerSession Session, int Generation, ulong Tag);

    /// <summary>
    /// State of one channel + consumer. <see cref="OnBrokerEvent"/> tells the two broker-side events apart:
    /// on a live connection the subscription is really gone (recreate it, stop retrying its deliveries); on a lost
    /// connection automatic recovery restores it, so only the tag generation advances.
    /// </summary>
    /// <remarks>
    /// Not disposed on purpose: a handler from an ended session may still hold its token. Sessions are created once per
    /// (re)subscription, so the linked registration on the stopping token is negligible.
    /// </remarks>
    private sealed class ConsumerSession
    {
        private readonly CancellationTokenSource _lost;
        private readonly TaskCompletionSource<string> _reason = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _generation;

        public ConsumerSession(CancellationToken stoppingToken) =>
            _lost = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        public int Generation => Volatile.Read(ref _generation);

        public bool IsLost => _reason.Task.IsCompleted;

        public CancellationToken DeliveryToken => _lost.Token;

        public Task<string> Lost => _reason.Task;

        public void OnBrokerEvent(bool connectionOpen, string reason)
        {
            if (!connectionOpen)
            {
                Interlocked.Increment(ref _generation);
                return;
            }

            if (_reason.TrySetResult(reason))
            {
                _lost.Cancel();
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Lost the subscription to {Queue} ({Reason}); recreating channel and consumer")]
    private static partial void LogSubscriptionLost(ILogger logger, string queue, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not start consuming {Queue} (consecutive failure {Failures}); retrying")]
    private static partial void LogSetupFailed(ILogger logger, Exception exception, string queue, int failures);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stopped processing {MessageId} on {Queue}: its channel or consumer is gone, the broker has requeued it")]
    private static partial void LogAbandoned(ILogger logger, string queue, string? messageId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Delivery {DeliveryTag} on {Queue} not settled: its channel closed or recovered since it arrived (the broker requeued it)")]
    private static partial void LogNotSettled(ILogger logger, string queue, ulong deliveryTag);

    [LoggerMessage(Level = LogLevel.Information, Message = "Consuming {Queue} (prefetch 1, manual ack)")]
    private static partial void LogConsuming(ILogger logger, string queue);

    [LoggerMessage(Level = LogLevel.Error, Message = "Poison message {MessageId} on {Queue}; dead-lettering it")]
    private static partial void LogPoison(ILogger logger, Exception exception, string queue, string? messageId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Infrastructure failure on {Queue} for {MessageId} (attempt {Attempt}); retrying in {DelaySeconds}s until it recovers")]
    private static partial void LogInfrastructure(ILogger logger, Exception exception, string queue, string? messageId, int attempt, double delaySeconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unexpected failure on {Queue} for {MessageId} (retry {Retry} in {DelaySeconds}s)")]
    private static partial void LogUnexpected(ILogger logger, Exception exception, string queue, string? messageId, int retry, double delaySeconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "Still failing on {Queue} for {MessageId} after retries; requeueing")]
    private static partial void LogRequeue(ILogger logger, Exception exception, string queue, string? messageId);
}
