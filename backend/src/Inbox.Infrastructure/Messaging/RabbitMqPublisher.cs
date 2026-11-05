using System.Text.Json;
using Inbox.Infrastructure.Serialization;
using RabbitMQ.Client;

namespace Inbox.Infrastructure.Messaging;

/// <summary>
/// Publishes to RabbitMQ (contracts §7.5).
/// Work messages: persistent, mandatory, awaited publisher confirm with a 5 s timeout; a nack or basic.return throws.
/// Events: transient, not mandatory, no confirms.
/// </summary>
public sealed class RabbitMqPublisher(RabbitMqConnection connection) : IAsyncDisposable
{
    public static readonly TimeSpan ConfirmTimeout = TimeSpan.FromSeconds(5);
    private const string JsonContentType = "application/json";

    private readonly ChannelSlot _confirmed = new(connection, confirms: true);
    private readonly ChannelSlot _events = new(connection, confirms: false);

    public async Task PublishConfirmedAsync<T>(string exchange, string routingKey, string type, string messageId, T message, CancellationToken cancellationToken)
    {
        var properties = new BasicProperties
        {
            ContentType = JsonContentType,
            DeliveryMode = DeliveryModes.Persistent,
            Type = type,
            MessageId = messageId,
        };
        var body = JsonSerializer.SerializeToUtf8Bytes(message, InboxJson.Options);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ConfirmTimeout);
        try
        {
            // The client does not always honour cancellation while the broker is unresponsive (channel open/close wait
            // for its own continuation timeout), so the 5 s bound is also enforced on the caller's side.
            await _confirmed.PublishAsync(exchange, routingKey, mandatory: true, properties, body, timeout.Token)
                .WaitAsync(ConfirmTimeout, cancellationToken);
        }
        catch (Exception ex) when (ex is TimeoutException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new TimeoutException($"The broker did not confirm the publish to '{exchange}' within {ConfirmTimeout.TotalSeconds:0} s.", ex);
        }
    }

    public Task PublishEventAsync<T>(string exchange, string type, T message, CancellationToken cancellationToken)
    {
        var properties = new BasicProperties
        {
            ContentType = JsonContentType,
            DeliveryMode = DeliveryModes.Transient,
            Type = type,
        };
        var body = JsonSerializer.SerializeToUtf8Bytes(message, InboxJson.Options);
        return _events.PublishAsync(exchange, string.Empty, mandatory: false, properties, body, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _confirmed.DisposeAsync();
        await _events.DisposeAsync();
    }

    /// <summary>One lazily created channel; an <see cref="IChannel"/> must not publish concurrently, so access is serialized.</summary>
    private sealed class ChannelSlot(RabbitMqConnection connection, bool confirms) : IAsyncDisposable
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private IChannel? _channel;

        public async Task PublishAsync(string exchange, string routingKey, bool mandatory, BasicProperties properties, byte[] body, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                var channel = await GetChannelAsync(cancellationToken);
                try
                {
                    await channel.BasicPublishAsync(exchange, routingKey, mandatory, properties, body, cancellationToken);
                }
                catch
                {
                    // A timed-out or failed publish may leave confirms in flight; the next publish gets a fresh channel.
                    // Closing waits for the broker, so the broken channel is disposed in the background.
                    var broken = _channel;
                    _channel = null;
                    _ = DisposeQuietlyAsync(broken);
                    throw;
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await ResetAsync();
            _gate.Dispose();
        }

        private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
        {
            if (_channel is { IsOpen: true } open)
            {
                return open;
            }

            await ResetAsync();
            var amqp = await connection.GetAsync(cancellationToken);
            _channel = await amqp.CreateChannelAsync(
                new CreateChannelOptions(publisherConfirmationsEnabled: confirms, publisherConfirmationTrackingEnabled: confirms),
                cancellationToken);
            return _channel;
        }

        private static async Task DisposeQuietlyAsync(IChannel? channel)
        {
            try
            {
                if (channel is not null)
                {
                    await channel.DisposeAsync();
                }
            }
            catch (Exception)
            {
                // Best effort: the channel is already unusable.
            }
        }

        private async Task ResetAsync()
        {
            var channel = _channel;
            _channel = null;
            if (channel is not null)
            {
                await channel.DisposeAsync();
            }
        }
    }
}
