using Inbox.Domain.Messages;

namespace Inbox.Application.Contracts;

/// <summary>Wire shape of a message (contracts §2). Enums serialize as camelCase strings.</summary>
public sealed record MessageDto(
    Guid Id,
    long Seq,
    Guid ContactId,
    MessageDirection Direction,
    string Text,
    DateTimeOffset OccurredAt,
    DateTimeOffset? ReadAt,
    DeliveryStatus Status,
    string? FailureReason)
{
    public static MessageDto From(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new MessageDto(
            message.Id,
            message.Seq,
            message.ContactId,
            message.Direction,
            message.Text.Value,
            message.OccurredAt,
            message.ReadAt,
            message.Status,
            message.Status == DeliveryStatus.Failed ? message.FailureReason : null);
    }
}

/// <summary>Wire shape of a contact in the sidebar (contracts §2).</summary>
public sealed record ContactSummaryDto(
    Guid Id,
    string DisplayName,
    string? Username,
    int UnreadCount,
    MessageDto? LastMessage);

/// <summary>Payload of messageReceived, messageQueued and messageStatusChanged (contracts §3).</summary>
public sealed record MessageEventPayload(MessageDto Message, ContactSummaryDto Contact);

/// <summary>Payload of conversationRead (contracts §3).</summary>
public sealed record ConversationReadPayload(ContactSummaryDto Contact);
