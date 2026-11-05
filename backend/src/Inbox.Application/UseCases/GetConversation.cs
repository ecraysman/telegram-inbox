using Inbox.Application.Abstractions;
using Inbox.Application.Contracts;
using Inbox.Application.Errors;

namespace Inbox.Application.UseCases;

public sealed class GetConversationHandler(IInboxQueries queries)
{
    public async Task<IReadOnlyList<MessageDto>> HandleAsync(Guid contactId, CancellationToken cancellationToken) =>
        await queries.GetConversationAsync(contactId, cancellationToken)
            ?? throw new ContactNotFoundException(contactId);
}
