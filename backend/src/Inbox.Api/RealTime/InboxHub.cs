using Microsoft.AspNetCore.SignalR;

namespace Inbox.Api.RealTime;

/// <summary>Server-to-client only hub at <c>/hubs/inbox</c> (ADR-002). Clients invoke no methods.</summary>
internal sealed class InboxHub : Hub
{
    public const string Path = "/hubs/inbox";
}
