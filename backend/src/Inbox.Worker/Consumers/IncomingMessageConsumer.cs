using Inbox.Application.Contracts;
using Inbox.Application.UseCases;
using Inbox.Infrastructure.Messaging;

namespace Inbox.Worker.Consumers;

/// <summary>Consumes <c>inbox.incoming.telegram</c> and persists each message (architecture §3).</summary>
internal sealed class IncomingMessageConsumer(
    RabbitMqConnection connection,
    IServiceScopeFactory scopes,
    ILogger<IncomingMessageConsumer> logger)
    : QueueConsumer<IncomingTextMessage>(connection, scopes, logger)
{
    protected override string QueueName => RabbitMqTopology.IncomingQueue;

    protected override Task HandleAsync(IServiceProvider services, IncomingTextMessage message, CancellationToken cancellationToken) =>
        services.GetRequiredService<ProcessIncomingMessageHandler>().HandleAsync(message, cancellationToken);
}
