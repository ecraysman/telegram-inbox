using Inbox.Application.Abstractions;
using Inbox.Application.Contracts;

namespace Inbox.Application.UseCases;

/// <summary>Webhook intake: hands a mapped Telegram text message to the inbound queue (confirmed publish).</summary>
public sealed class AcceptIncomingMessageHandler(IIncomingMessagePublisher publisher)
{
    public Task HandleAsync(IncomingTextMessage message, CancellationToken cancellationToken) =>
        publisher.PublishAsync(message, cancellationToken);
}
