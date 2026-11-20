using SqlSugar;

namespace DatabaseMcpServer.Models;

/// <summary>A single-owner lease. Do not use its client after returning the lease.</summary>
public sealed class DatabaseClientLease(DatabaseClientContext context, Action release) : IDisposable
{
    private Action? _release = release;
    public DatabaseClientContext Context { get; } = context;
    public ISqlSugarClient Client => Context.Client;
    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}
