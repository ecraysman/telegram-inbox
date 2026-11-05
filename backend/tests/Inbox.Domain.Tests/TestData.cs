using Inbox.Domain.Contacts;
using Inbox.Domain.Messages;

namespace Inbox.Domain.Tests;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 11, 5, 14, 3, 22, TimeSpan.Zero);

    public static MessageText Text(string value = "Hola") => MessageText.Create(value);

    public static Contact Contact(long chatId = 987654321) => Domain.Contacts.Contact.FromTelegram(chatId, "Ana", "Pérez", "anap", Now);

    public static Message Inbound(Guid contactId, long updateId = 1, DateTimeOffset? sentAt = null) =>
        Message.Inbound(contactId, updateId, 42, Text(), sentAt ?? Now);

    public static Message Outbound(Guid contactId) => Message.Outbound(contactId, Text("Respuesta"), Now);

    /// <summary>Simulates the database assigning the identity column.</summary>
    public static Message WithSeq(this Message message, long seq)
    {
        typeof(Message).GetProperty(nameof(Message.Seq))!.SetValue(message, seq);
        return message;
    }
}
