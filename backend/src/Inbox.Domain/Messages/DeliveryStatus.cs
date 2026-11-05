namespace Inbox.Domain.Messages;

/// <summary>Inbound messages are always <see cref="Received"/>; outbound ones follow Pending → Sent | Failed, Failed → Pending.</summary>
public enum DeliveryStatus
{
    Received,
    Pending,
    Sent,
    Failed,
}
