using System.Data.Common;

namespace Inbox.Infrastructure.Persistence;

/// <summary>
/// The single definition of a transient database error: a <see cref="DbException"/> with <c>IsTransient</c> (Npgsql sets
/// it for connection, timeout, 08xxx/53xxx/57Pxx/40001) or a <see cref="TimeoutException"/>, anywhere in the chain,
/// because EF Core wraps provider errors (DbUpdateException, "transient failure" InvalidOperationException).
/// </summary>
internal static class TransientErrors
{
    public static bool IsTransient(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException { IsTransient: true } or TimeoutException)
            {
                return true;
            }
        }

        return false;
    }
}
