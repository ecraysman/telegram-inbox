using Inbox.Domain.Common;

namespace Inbox.Domain.Messages;

/// <summary>
/// A message in a conversation. Inbound messages come from Telegram and start unread.
/// Outbound replies are born read and follow the delivery state machine Pending → Sent | Failed, Failed → Pending.
/// </summary>
public sealed class Message
{
    private Message(Guid id, Guid contactId, MessageDirection direction, MessageText text, DateTimeOffset occurredAt, DeliveryStatus status)
    {
        Id = id;
        ContactId = contactId;
        Direction = direction;
        Text = text;
        OccurredAt = occurredAt;
        Status = status;
    }

    public Guid Id { get; private set; }

    /// <summary>Database identity that breaks ordering ties; never written by application code.</summary>
    public long Seq { get; private set; }

    public Guid ContactId { get; private set; }

    public MessageDirection Direction { get; private set; }

    public MessageText Text { get; private set; }

    /// <summary>Inbound: Telegram's date. Outbound: when the operator queued the reply.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }

    public DeliveryStatus Status { get; private set; }

    public string? FailureReason { get; private set; }

    public long? TelegramUpdateId { get; private set; }

    public long? TelegramMessageId { get; private set; }

    public DateTimeOffset? DeliveredAt { get; private set; }

    /// <summary>Outbound: last time the reply was handed to the queue; the outbox sweeper's staleness key.</summary>
    public DateTimeOffset? LastQueuedAt { get; private set; }

    public bool IsUnread => ReadAt is null;

    public static Message Inbound(Guid contactId, long telegramUpdateId, long telegramMessageId, MessageText text, DateTimeOffset sentAt)
    {
        EnsureContact(contactId);
        ArgumentNullException.ThrowIfNull(text);
        if (telegramUpdateId <= 0)
        {
            throw new DomainException("An inbound message needs a positive Telegram update id.");
        }

        return new Message(Guid.CreateVersion7(), contactId, MessageDirection.Inbound, text, UtcGuard.EnsureUtc(sentAt, nameof(sentAt)), DeliveryStatus.Received)
        {
            TelegramUpdateId = telegramUpdateId,
            TelegramMessageId = telegramMessageId,
        };
    }

    public static Message Outbound(Guid contactId, MessageText text, DateTimeOffset queuedAt)
    {
        EnsureContact(contactId);
        ArgumentNullException.ThrowIfNull(text);
        queuedAt = UtcGuard.EnsureUtc(queuedAt, nameof(queuedAt));

        // An operator's own reply is never unread (single unread predicate: read_at IS NULL).
        return new Message(Guid.CreateVersion7(), contactId, MessageDirection.Outbound, text, queuedAt, DeliveryStatus.Pending)
        {
            ReadAt = queuedAt,
            LastQueuedAt = queuedAt,
        };
    }

    /// <summary>Marks the message as read. Idempotent: returns <c>false</c> when it was already read.</summary>
    public bool MarkAsRead(DateTimeOffset readAt)
    {
        readAt = UtcGuard.EnsureUtc(readAt, nameof(readAt));
        if (ReadAt is not null)
        {
            return false;
        }

        ReadAt = readAt;
        return true;
    }

    /// <summary>True when this message sorts at or before <paramref name="other"/> by (OccurredAt, Seq).</summary>
    public bool IsAtOrBefore(Message other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var byTime = OccurredAt.CompareTo(other.OccurredAt);
        return byTime < 0 || (byTime == 0 && Seq <= other.Seq);
    }

    public void MarkSent(long telegramMessageId, DateTimeOffset deliveredAt)
    {
        EnsureOutboundIn(DeliveryStatus.Pending, nameof(MarkSent));
        Status = DeliveryStatus.Sent;
        TelegramMessageId = telegramMessageId;
        DeliveredAt = UtcGuard.EnsureUtc(deliveredAt, nameof(deliveredAt));
        FailureReason = null;
    }

    public void MarkFailed(string reason)
    {
        EnsureOutboundIn(DeliveryStatus.Pending, nameof(MarkFailed));
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("A failed delivery needs a reason.");
        }

        Status = DeliveryStatus.Failed;
        FailureReason = reason;
    }

    /// <summary>Operator retry: Failed → Pending.</summary>
    public void RequeueForRetry(DateTimeOffset queuedAt)
    {
        EnsureOutboundIn(DeliveryStatus.Failed, nameof(RequeueForRetry));
        Status = DeliveryStatus.Pending;
        FailureReason = null;
        LastQueuedAt = UtcGuard.EnsureUtc(queuedAt, nameof(queuedAt));
    }

    /// <summary>The outbox sweeper handed a still-Pending reply to the queue again.</summary>
    public void RecordRequeued(DateTimeOffset queuedAt)
    {
        EnsureOutboundIn(DeliveryStatus.Pending, nameof(RecordRequeued));
        LastQueuedAt = UtcGuard.EnsureUtc(queuedAt, nameof(queuedAt));
    }

    private void EnsureOutboundIn(DeliveryStatus expected, string operation)
    {
        if (Direction != MessageDirection.Outbound)
        {
            throw new DomainException($"{operation} only applies to outbound messages.");
        }

        if (Status != expected)
        {
            throw new DomainException($"{operation} requires status {expected}, but the message is {Status}.");
        }
    }

    private static void EnsureContact(Guid contactId)
    {
        if (contactId == Guid.Empty)
        {
            throw new DomainException("A message must belong to a contact.");
        }
    }
}
