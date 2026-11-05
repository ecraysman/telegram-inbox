namespace Inbox.Application.Contracts;

/// <summary>Internal queue contract <c>OutgoingTextMessage.v1</c>. The database row is the source of truth.</summary>
public sealed record OutgoingTextMessage(Guid MessageId)
{
    public const string TypeName = "OutgoingTextMessage.v1";
}
