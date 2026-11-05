using Inbox.Application.Abstractions;
using Microsoft.EntityFrameworkCore.Storage;

namespace Inbox.Infrastructure.Persistence;

internal sealed class EfUnitOfWork(InboxDbContext db) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public async Task<ITransactionScope> BeginTransactionAsync(CancellationToken cancellationToken) =>
        new EfTransactionScope(await db.Database.BeginTransactionAsync(cancellationToken));

    private sealed class EfTransactionScope(IDbContextTransaction transaction) : ITransactionScope
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
