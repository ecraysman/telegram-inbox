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
        var rows = await SummaryRows(db.Contacts.AsNoTracking(), orderByLastActivity: true).ToListAsync(cancellationToken);
        return rows.Select(ToSummary).ToList();
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

    /// <summary>
    /// One row per contact with its unread count and last message (by OccurredAt, then Seq). With
    /// <paramref name="orderByLastActivity"/> the database sorts by that last message, newest first; contacts without
    /// messages go last (PostgreSQL would otherwise put NULLs first in a descending sort).
    /// </summary>
    private IQueryable<SummaryRow> SummaryRows(IQueryable<Contact> contacts, bool orderByLastActivity = false)
    {
        var rows = contacts.Select(c => new
        {
            Contact = c,
            UnreadCount = db.Messages.Count(m => m.ContactId == c.Id && m.ReadAt == null),
            LastMessage = db.Messages
                .Where(m => m.ContactId == c.Id)
                .OrderByDescending(m => m.OccurredAt).ThenByDescending(m => m.Seq)
                .FirstOrDefault(),
        });

        if (orderByLastActivity)
        {
            rows = rows
                .OrderBy(r => r.LastMessage == null)
                .ThenByDescending(r => r.LastMessage!.OccurredAt)
                .ThenByDescending(r => r.LastMessage!.Seq);
        }

        return rows.Select(r => new SummaryRow(r.Contact, r.UnreadCount, r.LastMessage));
    }

    private static ContactSummaryDto ToSummary(SummaryRow row) => new(
        row.Contact.Id,
        row.Contact.DisplayName,
        row.Contact.Username,
        row.UnreadCount,
        row.LastMessage is null ? null : MessageDto.From(row.LastMessage));

    private sealed record SummaryRow(Contact Contact, int UnreadCount, Message? LastMessage);
}
