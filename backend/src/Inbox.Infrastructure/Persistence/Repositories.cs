using Inbox.Application.Abstractions;
using Inbox.Domain.Contacts;
using Inbox.Domain.Messages;
using Microsoft.EntityFrameworkCore;

namespace Inbox.Infrastructure.Persistence;

internal sealed class ContactRepository(InboxDbContext db) : IContactRepository
{
    public Task<Contact?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Contacts.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<Contact?> GetByTelegramChatIdAsync(long chatId, CancellationToken cancellationToken) =>
        db.Contacts.FirstOrDefaultAsync(c => c.TelegramChatId == chatId, cancellationToken);

    public void Add(Contact contact) => db.Contacts.Add(contact);
}

internal sealed class MessageRepository(InboxDbContext db) : IMessageRepository
{
    private static readonly string Inbound = LowercaseEnumConverter<MessageDirection>.ToProvider(MessageDirection.Inbound);
    private static readonly string Received = LowercaseEnumConverter<DeliveryStatus>.ToProvider(DeliveryStatus.Received);

    public Task<Message?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Messages.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    public Task<Message?> GetByTelegramUpdateIdAsync(long updateId, CancellationToken cancellationToken) =>
        db.Messages.FirstOrDefaultAsync(m => m.TelegramUpdateId == updateId, cancellationToken);

    /// <summary>
    /// <c>INSERT … ON CONFLICT (telegram_update_id) DO NOTHING</c> (contracts §7.4): a duplicate never raises 23505,
    /// so the surrounding transaction is never aborted.
    /// </summary>
    public async Task<bool> TryInsertInboundAsync(Message message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Direction != MessageDirection.Inbound || message.TelegramUpdateId is null)
        {
            throw new ArgumentException("Only inbound Telegram messages use the idempotent insert.", nameof(message));
        }

        var rows = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO messages (id, contact_id, direction, text, occurred_at, status, telegram_update_id, telegram_message_id)
            VALUES ({message.Id}, {message.ContactId}, {Inbound}, {message.Text.Value}, {message.OccurredAt}, {Received}, {message.TelegramUpdateId.Value}, {message.TelegramMessageId})
            ON CONFLICT (telegram_update_id) DO NOTHING
            """,
            cancellationToken);
        return rows == 1;
    }

    public void Add(Message message) => db.Messages.Add(message);

    public async Task<IReadOnlyList<Message>> ListUnreadAsync(Guid contactId, CancellationToken cancellationToken) =>
        await db.Messages.Where(m => m.ContactId == contactId && m.ReadAt == null).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Message>> ListPendingQueuedBeforeAsync(DateTimeOffset threshold, CancellationToken cancellationToken) =>
        await db.Messages
            .Where(m => m.Status == DeliveryStatus.Pending && m.LastQueuedAt < threshold)
            .OrderBy(m => m.LastQueuedAt)
            .ToListAsync(cancellationToken);
}
