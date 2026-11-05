using Inbox.Application.Contracts;

namespace Inbox.Infrastructure.Telegram;

/// <summary>Anti-corruption layer: Telegram <c>Update</c> → <see cref="IncomingTextMessage"/> (D-013, D-025).</summary>
public static class TelegramUpdateMapper
{
    /// <summary>Maps a private-chat text <c>message</c>; anything else (edits, groups, channels, media) yields <c>null</c>.</summary>
    public static IncomingTextMessage? TryMap(TelegramUpdate? update)
    {
        if (update is not { UpdateId: > 0, Message: { Chat: { } chat, Text: { Length: > 0 } text } message }
            || !string.Equals(chat.Type, "private", StringComparison.Ordinal))
        {
            return null;
        }

        var sender = new IncomingSender(
            message.From?.FirstName ?? chat.FirstName ?? string.Empty,
            message.From?.LastName ?? chat.LastName,
            message.From?.Username ?? chat.Username);

        return new IncomingTextMessage(
            update.UpdateId,
            chat.Id,
            message.MessageId,
            sender,
            text,
            DateTimeOffset.FromUnixTimeSeconds(message.Date));
    }
}
