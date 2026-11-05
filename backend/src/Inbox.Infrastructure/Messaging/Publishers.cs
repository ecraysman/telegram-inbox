using System.Globalization;
using Inbox.Application.Abstractions;
using Inbox.Application.Contracts;

namespace Inbox.Infrastructure.Messaging;

internal sealed class IncomingMessagePublisher(RabbitMqPublisher publisher) : IIncomingMessagePublisher
{
    public Task PublishAsync(IncomingTextMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        return publisher.PublishConfirmedAsync(
            RabbitMqTopology.IncomingExchange,
            RabbitMqTopology.IncomingRoutingKey,
            IncomingTextMessage.TypeName,
            string.Create(CultureInfo.InvariantCulture, $"tg-update-{message.UpdateId}"),
            message,
            cancellationToken);
    }
}

internal sealed class OutgoingMessagePublisher(RabbitMqPublisher publisher) : IOutgoingMessagePublisher
{
    public Task PublishAsync(OutgoingTextMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        return publisher.PublishConfirmedAsync(
            RabbitMqTopology.OutgoingExchange,
            RabbitMqTopology.OutgoingRoutingKey,
            OutgoingTextMessage.TypeName,
            message.MessageId.ToString(),
            message,
            cancellationToken);
    }
}
