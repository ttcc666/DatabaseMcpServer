namespace DatabaseMcpServer.Models;

public sealed class DatabaseOperationLease(DatabaseOperationContext context, Action release) : IDisposable
{
    private Action? _release = release;
    public DatabaseOperationContext Context { get; } = context;
    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}
