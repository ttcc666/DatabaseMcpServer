namespace DatabaseMcpServer.Models;

/// <summary>Captures a connection version before waiting. Acquire once or dispose without acquiring.</summary>
public sealed class DatabaseOperationReservation(
    Func<CancellationToken, DatabaseOperationLease> acquire,
    Func<CancellationToken, ValueTask<DatabaseOperationLease>> acquireAsync,
    Action abandon,
    Func<DatabaseOperationReservation>? createAttempt = null) : IDisposable
{
    private readonly object _gate = new();
    private int _claimed;
    /// <summary>Reserve another attempt against this pinned snapshot without occupying an execution slot.</summary>
    public DatabaseOperationReservation CreateAttempt()
    {
        lock (_gate)
        {
            if (_claimed != 0) throw new ObjectDisposedException(nameof(DatabaseOperationReservation));
            return createAttempt?.Invoke() ?? throw new NotSupportedException("此预留不支持重复尝试。");
        }
    }
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
        lock (_gate)
        {
            if (_claimed != 0) throw new InvalidOperationException("连接预留只能使用一次。");
            _claimed = 1;
        }
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_claimed != 0) return;
            _claimed = 1;
            abandon();
        }
    }
}
