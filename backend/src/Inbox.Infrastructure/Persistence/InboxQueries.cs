using Inbox.Application.Abstractions;
using Inbox.Application.Contracts;
using Inbox.Domain.Contacts;
using Inbox.Domain.Messages;
using Microsoft.EntityFrameworkCore;

namespace Inbox.Infrastructure.Persistence;

/// <summary>Read projections; unread count and last message are computed at query time (D-022).</summary>
internal sealed class InboxQueries(InboxDbContext db) : IInboxQueries
{
    public async Task<IReadOnlyList<ContactSummaryDto>> GetContactsAsync(CancellationToken cancellationToken)
    {
        var rows = await SummaryRows(db.Contacts.AsNoTracking()).ToListAsync(cancellationToken);
        return rows
            .Select(ToSummary)
            .OrderByDescending(s => s.LastMessage?.OccurredAt)
            .ThenByDescending(s => s.LastMessage?.Seq)
            .ToList();
    }

    public async Task<ContactSummaryDto?> GetContactSummaryAsync(Guid contactId, CancellationToken cancellationToken)
    {
        var row = await SummaryRows(db.Contacts.AsNoTracking().Where(c => c.Id == contactId))
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : ToSummary(row);
    }

    public async Task<IReadOnlyList<MessageDto>?> GetConversationAsync(Guid contactId, CancellationToken cancellationToken)
    {
        if (!await db.Contacts.AnyAsync(c => c.Id == contactId, cancellationToken))
        {
            return null;
        }

        var messages = await db.Messages.AsNoTracking()
            .Where(m => m.ContactId == contactId)
            .OrderBy(m => m.OccurredAt).ThenBy(m => m.Seq)
            .ToListAsync(cancellationToken);
        return messages.Select(MessageDto.From).ToList();
    }

    public async Task<MessageDto?> GetMessageAsync(Guid messageId, CancellationToken cancellationToken)
    {
        var message = await db.Messages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);
        return message is null ? null : MessageDto.From(message);
    }

    private IQueryable<SummaryRow> SummaryRows(IQueryable<Contact> contacts) =>
        contacts.Select(c => new SummaryRow(
            c,
            db.Messages.Count(m => m.ContactId == c.Id && m.ReadAt == null),
            db.Messages
                .Where(m => m.ContactId == c.Id)
                .OrderByDescending(m => m.OccurredAt).ThenByDescending(m => m.Seq)
                .FirstOrDefault()));

    private static ContactSummaryDto ToSummary(SummaryRow row) => new(
        row.Contact.Id,
        row.Contact.DisplayName,
        row.Contact.Username,
        row.UnreadCount,
        row.LastMessage is null ? null : MessageDto.From(row.LastMessage));

    private sealed record SummaryRow(Contact Contact, int UnreadCount, Message? LastMessage);
}
