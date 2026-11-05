using System.Text.Json;
using Inbox.Application.Abstractions;
using Inbox.Application.Contracts;
using Inbox.Infrastructure.Messaging;
using Microsoft.AspNetCore.SignalR;

namespace Inbox.Api.RealTime;

/// <summary>Api-side notifier: events originated in the api (queued, read, retry) go straight to the hub.</summary>
internal sealed class SignalRInboxNotifier(IHubContext<InboxHub> hub) : IInboxNotifier
{
    public Task MessageReceivedAsync(MessageEventPayload payload, CancellationToken cancellationToken) =>
        hub.Clients.All.SendAsync(InboxEventNames.MessageReceived, payload, cancellationToken);

    public Task MessageQueuedAsync(MessageEventPayload payload, CancellationToken cancellationToken) =>
        hub.Clients.All.SendAsync(InboxEventNames.MessageQueued, payload, cancellationToken);

    public Task MessageStatusChangedAsync(MessageEventPayload payload, CancellationToken cancellationToken) =>
        hub.Clients.All.SendAsync(InboxEventNames.MessageStatusChanged, payload, cancellationToken);

    public Task ConversationReadAsync(ConversationReadPayload payload, CancellationToken cancellationToken) =>
        hub.Clients.All.SendAsync(InboxEventNames.ConversationRead, payload, cancellationToken);
}

/// <summary>The <c>inbox.events</c> relay target: forwards the worker's payload unchanged (contracts §7.1).</summary>
internal sealed class SignalRInboxEventSink(IHubContext<InboxHub> hub) : IInboxEventSink
{
    public Task ForwardAsync(string eventType, JsonElement payload, CancellationToken cancellationToken) =>
        hub.Clients.All.SendAsync(eventType, payload, cancellationToken);
}
