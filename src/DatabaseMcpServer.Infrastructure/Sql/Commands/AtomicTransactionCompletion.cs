using System.Data.Common;

namespace DatabaseMcpServer.Services;

// The completion boundary can fail after the database has already acted.
internal class AtomicTransactionCompletion
{
    public virtual Task CommitAsync(DbTransaction transaction, CancellationToken token) => transaction.CommitAsync(token);
    public virtual Task RollbackAsync(DbTransaction transaction, CancellationToken token) => transaction.RollbackAsync(token);
}
