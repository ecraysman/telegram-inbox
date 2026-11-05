using Inbox.Application.Contracts;
using Inbox.Application.UseCases;
using Inbox.Infrastructure.Messaging;

namespace Inbox.Worker.Consumers;

/// <summary>Consumes <c>inbox.outgoing.telegram</c> and delivers each reply to Telegram (architecture §6).</summary>
internal sealed class OutgoingMessageConsumer(
    RabbitMqConnection connection,
    IServiceScopeFactory scopes,
    ILogger<OutgoingMessageConsumer> logger)
    : QueueConsumer<OutgoingTextMessage>(connection, scopes, logger)
{
    protected override string QueueName => RabbitMqTopology.OutgoingQueue;

    protected override Task HandleAsync(IServiceProvider services, OutgoingTextMessage message, CancellationToken cancellationToken) =>
        services.GetRequiredService<DeliverReplyHandler>().HandleAsync(message, cancellationToken);
}
