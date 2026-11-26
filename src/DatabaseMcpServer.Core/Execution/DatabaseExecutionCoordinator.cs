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
    private readonly LinkedList<Waiter> _waiting = new();
    private int _active;
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
                _admissions[connection.Name] = admission = new Admission(connection.Name);
            return ReserveSnapshot(admission, connection.Copy());
        }
    }

    private DatabaseOperationReservation ReserveSnapshot(Admission admission, DatabaseConnection snapshot)
    {
        lock (_poolLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            admission.References++;
            return new(token => Acquire(admission, snapshot, token),
                token => AcquireAsync(admission, snapshot, token), () => ReleaseReference(admission),
                () => ReserveSnapshot(admission, snapshot));
        }
    }

    private DatabaseOperationLease Acquire(Admission admission, DatabaseConnection connection, CancellationToken token)
    {
        try { EnterAsync(admission, token).AsTask().GetAwaiter().GetResult(); }
        catch { ReleaseReference(admission); throw; }
        return CreateLease(admission, connection);
    }

    private async ValueTask<DatabaseOperationLease> AcquireAsync(Admission admission, DatabaseConnection connection, CancellationToken token)
    {
        try { await EnterAsync(admission, token).ConfigureAwait(false); }
        catch { ReleaseReference(admission); throw; }
        return CreateLease(admission, connection);
    }

    private DatabaseOperationLease CreateLease(Admission admission, DatabaseConnection connection)
    {
        try
        {
            lock (_poolLock) ObjectDisposedException.ThrowIf(_disposed, this);
            var context = new DatabaseOperationContext(connection, admission.Identity);
            return new(context, () => { Exit(admission); ReleaseReference(admission); });
        }
        catch { Exit(admission); ReleaseReference(admission); throw; }
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
                if (!_admissions.ContainsKey(name)) _admissions[name] = new Admission(name);
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

    private async ValueTask EnterAsync(Admission admission, CancellationToken token)
    {
        using var activity = DatabaseTelemetry.StartAdmission();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdown.Token);
        linked.Token.ThrowIfCancellationRequested();
        Waiter? waiter = null;
        Rejection? rejection = null;
        lock (_poolLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            linked.Token.ThrowIfCancellationRequested();
            if (admission.Queued == 0 && CanEnter(admission))
            {
                Grant(admission);
            }
            else if (_waiting.Count >= _options.MaxTotalQueued) rejection = Rejection.TotalQueue;
            else if (admission.Queued >= _options.MaxQueued) rejection = Rejection.ConnectionQueue;
            else
            {
                waiter = new(admission);
                waiter.Node = _waiting.AddLast(waiter);
                admission.Queued++;
            }
        }
        if (rejection != null) throw Busy(admission, rejection.Value);
        if (waiter != null)
        {
            DatabaseTelemetry.Queued.Add(1);
            try
            {
                var remaining = TimeSpan.FromSeconds(_options.QueueTimeoutSeconds) - Stopwatch.GetElapsedTime(waiter.Started);
                if (remaining <= TimeSpan.Zero) throw new TimeoutException();
                await waiter.Completion.Task.WaitAsync(remaining, linked.Token).ConfigureAwait(false);
                linked.Token.ThrowIfCancellationRequested();
            }
            catch (Exception ex)
            {
                var granted = false;
                lock (_poolLock)
                {
                    granted = waiter.Node == null;
                    if (!granted) RemoveWaiter(waiter);
                }
                // A release can grant the slot just as WaitAsync cancels. Return that grant exactly once.
                if (granted) Exit(admission, recordActive: false);
                if (ex is TimeoutException) throw Busy(admission, Rejection.Timeout);
                throw;
            }
            finally
            {
                DatabaseTelemetry.Queued.Add(-1);
                DatabaseTelemetry.QueueMs.Record(Stopwatch.GetElapsedTime(waiter.Started).TotalMilliseconds);
            }
        }
        DatabaseTelemetry.Active.Add(1);
    }

    private bool CanEnter(Admission admission)
        => _active < _options.MaxTotalConcurrent && admission.Active < _options.MaxConcurrent;

    // Called under _poolLock, including grants that race with cancellation.
    private void Grant(Admission admission)
    {
        admission.Active++;
        _active++;
    }

    private void RemoveWaiter(Waiter waiter)
    {
        _waiting.Remove(waiter.Node!);
        waiter.Node = null;
        waiter.Admission.Queued--;
    }

    private void Exit(Admission admission, bool recordActive = true)
    {
        if (recordActive) DatabaseTelemetry.Active.Add(-1);
        List<Waiter>? ready = null;
        lock (_poolLock)
        {
            admission.Active--;
            _active--;
            var node = _waiting.First;
            while (!_disposed && node != null && _active < _options.MaxTotalConcurrent)
            {
                var waiter = node.Value;
                node = node.Next;
                if (!CanEnter(waiter.Admission)) continue;
                RemoveWaiter(waiter);
                Grant(waiter.Admission);
                (ready ??= []).Add(waiter);
            }
        }
        if (ready != null)
            foreach (var waiter in ready) waiter.Completion.TrySetResult();
    }

    private static DatabaseMcpException Busy(Admission admission, Rejection reason)
    {
        var (tag, message) = reason switch
        {
            Rejection.TotalQueue => ("total_queue_full", "应用总等待队列已满"),
            Rejection.ConnectionQueue => ("connection_queue_full", "连接等待队列已满"),
            _ => ("queue_timeout", "等待超时")
        };
        DatabaseTelemetry.Rejected.Add(1, new KeyValuePair<string, object?>("reason", tag));
        return new(DatabaseErrorCode.DatabaseBusy, $"数据库 '{admission.Name}' 繁忙：{message}，请稍后重试。");
    }

    private enum Rejection { TotalQueue, ConnectionQueue, Timeout }

    private sealed class Admission(string name)
    {
        public string Name { get; } = name;
        public string Identity { get; } = Guid.NewGuid().ToString("N");
        public int References { get; set; }
        public int Active { get; set; }
        public int Queued { get; set; }
    }

    private sealed class Waiter(Admission admission)
    {
        public Admission Admission { get; } = admission;
        public long Started { get; } = Stopwatch.GetTimestamp();
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public LinkedListNode<Waiter>? Node { get; set; }
    }
}
