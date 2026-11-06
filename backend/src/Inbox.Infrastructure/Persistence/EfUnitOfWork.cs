using Inbox.Application.Abstractions;
using Inbox.Application.Errors;
using Microsoft.EntityFrameworkCore.Storage;

namespace Inbox.Infrastructure.Persistence;

/// <summary>Unit of work over EF Core. Transient provider errors surface as <see cref="TransientPersistenceException"/>.</summary>
internal sealed class EfUnitOfWork(InboxDbContext db) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        TranslateAsync(() => db.SaveChangesAsync(cancellationToken), "Saving changes");

    public async Task<ITransactionScope> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        IDbContextTransaction transaction = null!;
        await TranslateAsync(async () => transaction = await db.Database.BeginTransactionAsync(cancellationToken), "Beginning a transaction");
        return new EfTransactionScope(transaction);
    }

    internal static async Task TranslateAsync(Func<Task> operation, string what)
    {
        try
        {
            await operation();
        }
        catch (Exception ex) when (ex is not OperationCanceledException && TransientErrors.IsTransient(ex))
        {
            throw new TransientPersistenceException($"{what} failed with a transient database error.", ex);
        }
    }

    private sealed class EfTransactionScope(IDbContextTransaction transaction) : ITransactionScope
    {
        public Task CommitAsync(CancellationToken cancellationToken) =>
            TranslateAsync(() => transaction.CommitAsync(cancellationToken), "Committing a transaction");

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
