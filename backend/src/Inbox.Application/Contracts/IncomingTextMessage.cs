namespace Inbox.Application.Contracts;

/// <summary>Internal queue contract <c>IncomingTextMessage.v1</c> (anti-corruption layer over Telegram's Update).</summary>
public sealed record IncomingTextMessage(
    long UpdateId,
    long ChatId,
    long TelegramMessageId,
    IncomingSender From,
    string Text,
    DateTimeOffset SentAt)
{
    public const string TypeName = "IncomingTextMessage.v1";
}

public sealed record IncomingSender(string FirstName, string? LastName, string? Username);
