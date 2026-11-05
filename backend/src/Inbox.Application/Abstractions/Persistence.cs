using Inbox.Application.Contracts;
using Inbox.Domain.Contacts;
using Inbox.Domain.Messages;

namespace Inbox.Application.Abstractions;

public interface IContactRepository
{
    Task<Contact?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Contact?> GetByTelegramChatIdAsync(long chatId, CancellationToken cancellationToken);

    void Add(Contact contact);
}

public interface IMessageRepository
{
    Task<Message?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Message?> GetByTelegramUpdateIdAsync(long updateId, CancellationToken cancellationToken);

    /// <summary>Inserts an inbound message unless its Telegram update id already exists. Returns <c>false</c> on a duplicate.</summary>
    Task<bool> TryInsertInboundAsync(Message message, CancellationToken cancellationToken);

    void Add(Message message);

    Task<IReadOnlyList<Message>> ListUnreadAsync(Guid contactId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Message>> ListPendingQueuedBeforeAsync(DateTimeOffset threshold, CancellationToken cancellationToken);
}

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);

    Task<ITransactionScope> BeginTransactionAsync(CancellationToken cancellationToken);
}

public interface ITransactionScope : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}

/// <summary>Read-side projections for the inbox (computed at query time, D-022).</summary>
public interface IInboxQueries
{
    Task<IReadOnlyList<ContactSummaryDto>> GetContactsAsync(CancellationToken cancellationToken);

    Task<ContactSummaryDto?> GetContactSummaryAsync(Guid contactId, CancellationToken cancellationToken);

    /// <summary>Full history ordered by (OccurredAt, Seq), or <c>null</c> when the contact does not exist.</summary>
    Task<IReadOnlyList<MessageDto>?> GetConversationAsync(Guid contactId, CancellationToken cancellationToken);

    Task<MessageDto?> GetMessageAsync(Guid messageId, CancellationToken cancellationToken);
}
