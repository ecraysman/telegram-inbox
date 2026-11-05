using System.Globalization;
using Inbox.Domain.Common;
using Inbox.Domain.Messages;

namespace Inbox.Domain.Contacts;

/// <summary>A Telegram user who writes to the bot. Identity is the private chat id.</summary>
public sealed class Contact
{
    private Contact(Guid id, long telegramChatId, DateTimeOffset createdAt)
    {
        Id = id;
        TelegramChatId = telegramChatId;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public long TelegramChatId { get; private set; }

    public string FirstName { get; private set; } = string.Empty;

    public string? LastName { get; private set; }

    /// <summary>Telegram username without the leading '@'.</summary>
    public string? Username { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>"First Last" trimmed, else "@username", else the chat id.</summary>
    public string DisplayName
    {
        get
        {
            var fullName = $"{FirstName} {LastName}".Trim();
            if (fullName.Length > 0)
            {
                return fullName;
            }

            return Username is not null ? $"@{Username}" : TelegramChatId.ToString(CultureInfo.InvariantCulture);
        }
    }

    public static Contact FromTelegram(long telegramChatId, string? firstName, string? lastName, string? username, DateTimeOffset now)
    {
        if (telegramChatId == 0)
        {
            throw new DomainException("A contact needs a Telegram chat id.");
        }

        var contact = new Contact(Guid.CreateVersion7(), telegramChatId, UtcGuard.EnsureUtc(now, nameof(now)));
        contact.ApplyProfile(firstName, lastName, username);
        return contact;
    }

    /// <summary>Refreshes the profile with the latest data Telegram sent. Returns <c>true</c> if anything changed.</summary>
    public bool UpdateProfile(string? firstName, string? lastName, string? username, DateTimeOffset now)
    {
        now = UtcGuard.EnsureUtc(now, nameof(now));
        if (!ApplyProfile(firstName, lastName, username))
        {
            return false;
        }

        UpdatedAt = now;
        return true;
    }

    /// <summary>
    /// Marks as read every unread inbound message of this conversation that sorts at or before
    /// <paramref name="upTo"/> by (OccurredAt, Seq). Returns how many messages changed.
    /// </summary>
    public int MarkConversationReadUpTo(Message upTo, IEnumerable<Message> conversation, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(upTo);
        ArgumentNullException.ThrowIfNull(conversation);
        EnsureOwns(upTo);

        var changed = 0;
        foreach (var message in conversation)
        {
            EnsureOwns(message);
            if (message.IsAtOrBefore(upTo) && message.MarkAsRead(now))
            {
                changed++;
            }
        }

        return changed;
    }

    private void EnsureOwns(Message message)
    {
        if (message.ContactId != Id)
        {
            throw new DomainException("The message belongs to another contact.");
        }
    }

    private bool ApplyProfile(string? firstName, string? lastName, string? username)
    {
        var first = firstName?.Trim() ?? string.Empty;
        var last = Normalize(lastName);
        var user = Normalize(username?.TrimStart('@'));

        if (first == FirstName && last == LastName && user == Username)
        {
            return false;
        }

        FirstName = first;
        LastName = last;
        Username = user;
        return true;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
