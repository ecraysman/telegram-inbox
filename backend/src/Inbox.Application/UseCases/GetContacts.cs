using Inbox.Application.Abstractions;
using Inbox.Application.Contracts;

namespace Inbox.Application.UseCases;

public sealed class GetContactsHandler(IInboxQueries queries)
{
    /// <summary>Contacts ordered by the last message's OccurredAt, newest first.</summary>
    public Task<IReadOnlyList<ContactSummaryDto>> HandleAsync(CancellationToken cancellationToken) =>
        queries.GetContactsAsync(cancellationToken);
}
