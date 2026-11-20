namespace DatabaseMcpServer.Models;

/// <summary>Captures a connection version before waiting. Acquire once or dispose without acquiring.</summary>
public sealed class DatabaseOperationReservation(
    Func<CancellationToken, DatabaseOperationLease> acquire,
    Func<CancellationToken, ValueTask<DatabaseOperationLease>> acquireAsync,
    Action abandon) : IDisposable
{
    private int _claimed;
    public DatabaseOperationLease Acquire(CancellationToken cancellationToken = default)
    {
        Claim();
        return acquire(cancellationToken);
    }
    public ValueTask<DatabaseOperationLease> AcquireAsync(CancellationToken cancellationToken = default)
    {
        Claim();
        return acquireAsync(cancellationToken);
    }
    private void Claim()
    {
        if (Interlocked.Exchange(ref _claimed, 1) != 0) throw new InvalidOperationException("连接预留只能使用一次。");
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _claimed, 1) == 0) abandon();
    }
}
