using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;
using System.Diagnostics;

namespace DatabaseMcpServer.Services;

internal sealed class DatabaseExecutionCoordinator : IDisposable
{
    private readonly HashSet<string> _configuredNames = new(StringComparer.Ordinal);
    private readonly object _poolLock = new();
    private readonly Dictionary<string, Admission> _admissions = new(StringComparer.Ordinal);
    private (string Previous, string New)? _pendingRename;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly DatabaseExecutionOptions _options;
    private bool _disposed;

    public DatabaseExecutionCoordinator(DatabaseExecutionOptions options)
    {
        options.Validate();
        _options = options;
    }

    public DatabaseOperationReservation Reserve(DatabaseConnection connection)
    {
        lock (_poolLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_admissions.TryGetValue(connection.Name, out var admission))
                _admissions[connection.Name] = admission = new Admission(connection.Name, _options);
            var snapshot = connection.Copy();
            admission.References++;
            return new(token => Acquire(admission, snapshot, token),
                token => AcquireAsync(admission, snapshot, token), () => ReleaseReference(admission));
        }
    }

    private DatabaseOperationLease Acquire(Admission admission, DatabaseConnection connection, CancellationToken token)
    {
        try { admission.Enter(token, _shutdown.Token); }
        catch { ReleaseReference(admission); throw; }
        return CreateLease(admission, connection);
    }

    private async ValueTask<DatabaseOperationLease> AcquireAsync(Admission admission, DatabaseConnection connection, CancellationToken token)
    {
        try { await admission.EnterAsync(token, _shutdown.Token).ConfigureAwait(false); }
        catch { ReleaseReference(admission); throw; }
        return CreateLease(admission, connection);
    }

    private DatabaseOperationLease CreateLease(Admission admission, DatabaseConnection connection)
    {
        try
        {
            lock (_poolLock) ObjectDisposedException.ThrowIf(_disposed, this);
            var context = new DatabaseOperationContext(connection, admission.Identity);
            return new(context, () => { admission.Exit(); ReleaseReference(admission); });
        }
        catch { admission.Exit(); ReleaseReference(admission); throw; }
    }

    private void ReleaseReference(Admission admission)
    {
        lock (_poolLock)
        {
            admission.References--;
            PruneAdmissions();
        }
    }

    private void PruneAdmissions()
    {
        foreach (var (name, admission) in _admissions)
            if (admission.References == 0 && !_configuredNames.Contains(name)) _admissions.Remove(name);
    }

    public void UpdateConnections(IReadOnlyDictionary<string, DatabaseConnection> connections, string? previousName = null, string? newName = null)
    {
        lock (_poolLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_pendingRename is { } pending)
            {
                if (previousName == pending.New && newName != null && !connections.ContainsKey(pending.Previous))
                    previousName = pending.Previous;
                else if (!connections.ContainsKey(pending.Previous) && connections.ContainsKey(pending.New))
                    (previousName, newName) = pending;
                else
                    _pendingRename = null;
            }
            if (previousName != null && newName != null && previousName != newName && _admissions.TryGetValue(previousName, out var renamed))
            {
                // A removed name may still own executing or queued requests. Do not merge
                // independent admission identities while either target is still in use.
                if (_admissions.TryGetValue(newName, out var destination) && destination != renamed && destination.References != 0)
                {
                    _pendingRename = (previousName, newName);
                    throw new DatabaseMcpException(DatabaseErrorCode.DatabaseBusy,
                        $"数据库 '{newName}' 的旧请求尚未结束，请在请求结束后重新加载配置。");
                }
                _admissions.Remove(previousName);
                _admissions[newName] = renamed;
            }
            _pendingRename = null;
            _configuredNames.Clear();
            _configuredNames.UnionWith(connections.Keys);
            // Keep admission identities across configuration versions.
            foreach (var name in connections.Keys)
                if (!_admissions.ContainsKey(name)) _admissions[name] = new Admission(name, _options);
            foreach (var (name, admission) in _admissions)
                if (admission.References == 0 && !connections.ContainsKey(name)) _admissions.Remove(name);
        }
    }

    public void Dispose()
    {
        lock (_poolLock)
        {
            if (_disposed) return;
            _disposed = true;
            _configuredNames.Clear();
            PruneAdmissions();
        }
        _shutdown.Cancel();
    }

    private sealed class Admission(string name, DatabaseExecutionOptions options)
    {
        public string Identity { get; } = Guid.NewGuid().ToString("N");
        private readonly SemaphoreSlim _slots = new(options.MaxConcurrent, options.MaxConcurrent);
        private int _waiting;
        public int References { get; set; }

        public void Enter(CancellationToken token, CancellationToken shutdown)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, shutdown);
            linked.Token.ThrowIfCancellationRequested();
            if (_slots.Wait(0)) { DatabaseTelemetry.Active.Add(1); return; }
            StartWaiting();
            var started = Stopwatch.GetTimestamp();
            try
            {
                if (!_slots.Wait(TimeSpan.FromSeconds(options.QueueTimeoutSeconds), linked.Token)) throw Busy();
                DatabaseTelemetry.Active.Add(1);
            }
            finally { EndWaiting(started); }
        }

        public async ValueTask EnterAsync(CancellationToken token, CancellationToken shutdown)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, shutdown);
            linked.Token.ThrowIfCancellationRequested();
            if (_slots.Wait(0)) { DatabaseTelemetry.Active.Add(1); return; }
            StartWaiting();
            var started = Stopwatch.GetTimestamp();
            try
            {
                if (!await _slots.WaitAsync(TimeSpan.FromSeconds(options.QueueTimeoutSeconds), linked.Token).ConfigureAwait(false)) throw Busy();
                DatabaseTelemetry.Active.Add(1);
            }
            finally { EndWaiting(started); }
        }

        private void StartWaiting()
        {
            if (Interlocked.Increment(ref _waiting) <= options.MaxQueued)
            { DatabaseTelemetry.Queued.Add(1); return; }
            Interlocked.Decrement(ref _waiting);
            throw Busy();
        }
        private void EndWaiting(long started)
        {
            Interlocked.Decrement(ref _waiting);
            DatabaseTelemetry.Queued.Add(-1);
            DatabaseTelemetry.QueueMs.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        private DatabaseMcpException Busy()
        {
            DatabaseTelemetry.Rejected.Add(1);
            return new(DatabaseErrorCode.DatabaseBusy, $"数据库 '{name}' 繁忙：队列已满或等待超时，请稍后重试。");
        }
        public void Exit() { _slots.Release(); DatabaseTelemetry.Active.Add(-1); }
    }

}
