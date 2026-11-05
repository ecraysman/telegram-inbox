using System.Text.Json;

namespace Inbox.Infrastructure.Messaging;

/// <summary>Envelope on <c>inbox.events</c> (<c>InboxEvent.v1</c>). The relay forwards <see cref="Payload"/> unchanged.</summary>
public sealed record InboxEvent(string EventType, JsonElement Payload)
{
    public const string TypeName = "InboxEvent.v1";
}

/// <summary>Event names shared by the SignalR hub and the inbox.events envelope (contracts §3).</summary>
public static class InboxEventNames
{
    public const string MessageReceived = "messageReceived";
    public const string MessageQueued = "messageQueued";
    public const string MessageStatusChanged = "messageStatusChanged";
    public const string ConversationRead = "conversationRead";
}
