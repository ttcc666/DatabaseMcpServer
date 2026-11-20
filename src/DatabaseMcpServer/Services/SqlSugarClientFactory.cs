using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using Microsoft.Extensions.Logging;
using SqlSugar;
using System.Diagnostics;

namespace DatabaseMcpServer.Services;

internal sealed class SqlSugarClientFactory : ISqlSugarClientFactory, IDisposable
{
    private readonly ILogger<SqlSugarClientFactory> _logger;
    private readonly IDatabaseHelperService _databaseHelper;
    private readonly IDatabaseOptimizationStrategyFactory _strategyFactory;
    private readonly HashSet<string> _configuredNames = new(StringComparer.Ordinal);
    private readonly object _poolLock = new();
    private readonly Dictionary<string, Admission> _admissions = new(StringComparer.Ordinal);
    private (string Previous, string New)? _pendingRename;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly DatabaseExecutionOptions _options;
    private bool _disposed;

    public SqlSugarClientFactory(
        ILogger<SqlSugarClientFactory> logger,
        IDatabaseHelperService databaseHelper,
        IDatabaseOptimizationStrategyFactory strategyFactory, DatabaseExecutionOptions options)
    {
        _logger = logger;
        _databaseHelper = databaseHelper;
        _strategyFactory = strategyFactory;
        _options = options;
        _options.Validate();
    }

    public DatabaseClientLease AcquireClient(DatabaseConnection connection)
    {
        using var reservation = ReserveClient(connection);
        return reservation.Acquire();
    }

    public DatabaseClientReservation ReserveClient(DatabaseConnection connection)
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

    private DatabaseClientLease Acquire(Admission admission, DatabaseConnection connection, CancellationToken token)
    {
        try { admission.Enter(token, _shutdown.Token); }
        catch { ReleaseReference(admission); throw; }
        return CreateLease(admission, connection);
    }

    private async ValueTask<DatabaseClientLease> AcquireAsync(Admission admission, DatabaseConnection connection, CancellationToken token)
    {
        try { await admission.EnterAsync(token, _shutdown.Token).ConfigureAwait(false); }
        catch { ReleaseReference(admission); throw; }
        return CreateLease(admission, connection);
    }

    private DatabaseClientLease CreateLease(Admission admission, DatabaseConnection connection)
    {
        SqlSugarClient? client = null;
        try
        {
            lock (_poolLock) ObjectDisposedException.ThrowIf(_disposed, this);
            // SqlSugarClient is a per-operation context; physical connections are pooled by the driver.
            client = new SqlSugarClient(CreateConfig(connection));
            ConfigureAop(client);
            lock (_poolLock) ObjectDisposedException.ThrowIf(_disposed, this);
            return new(new DatabaseClientContext(client, connection.EnableDangerousOperations, admission.Identity)
                { DatabaseName = connection.Name, DatabaseType = connection.DbType,
                    ConfigurationVersion = connection.ConfigurationVersion, AccessMode = connection.AccessMode,
                    OptimizationSettings = connection.OptimizationSettings == null ? null : new Dictionary<string, string>(connection.OptimizationSettings) },
                () => Return(admission, client));
        }
        catch
        {
            if (client != null) DisposeClient(client);
            admission.Exit();
            ReleaseReference(admission);
            throw;
        }
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

    private ConnectionConfig CreateConfig(DatabaseConnection connection)
    {
        var dbType = _databaseHelper.ParseDbType(connection.DbType);
        DatabaseProviderCatalog.EnsureAvailable(dbType);
        if (DatabaseProviderCatalog.ConnectionStringError(dbType, connection.ConnectionString) is { } invalid)
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, invalid);
        SqlSugarProviderWarmup.Warmup(null);
        var errors = DatabaseOptimizationOptions.Errors(dbType, connection.OptimizationSettings).ToArray();
        if (errors.Length > 0)
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, string.Join("\n", errors));
        return new ConnectionConfig
        {
            ConnectionString = connection.ConnectionString, DbType = dbType,
            IsAutoCloseConnection = true, InitKeyType = InitKeyType.Attribute,
            MoreSettings = CreateOptimizedSettings(dbType, connection)
        };
    }

    private void Return(Admission admission, SqlSugarClient client)
    {
        DisposeClient(client);
        admission.Exit();
        ReleaseReference(admission);
    }

    private void DisposeClient(ISqlSugarClient client)
    {
        // Mongo leases never open SqlSugar ADO. Its Dispose can construct the legacy static
        // Mongo connection cache; native clients are owned and disposed by MongoClientRegistry.
        if (client.CurrentConnectionConfig.DbType == DbType.MongoDb) return;
        try { client.Dispose(); }
        catch (Exception ex) { _logger.LogWarning(ex, "释放失效数据库客户端失败"); }
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

    private ConnMoreSettings CreateOptimizedSettings(DbType dbType, DatabaseConnection connection)
    {
        var settings = new ConnMoreSettings
        {
            IsAutoRemoveDataCache = true,
            SqlServerCodeFirstNvarchar = true
        };

        var strategy = _strategyFactory.GetStrategy(dbType);
        strategy.ApplyOptimizations(settings, DatabaseOptimizationOptions.AppliedOptions(dbType, connection.OptimizationSettings));
        _logger.LogDebug("已为数据库 {Name} 配置 ORM 设置；连接池/TLS 等以驱动连接字符串为准", connection.Name);

        return settings;
    }

    private void ConfigureAop(SqlSugarClient db)
    {
        db.Aop.OnLogExecuted = (_, _) => DatabaseTelemetry.SqlMs.Record(db.Ado.SqlExecutionTime.TotalMilliseconds);
        db.Aop.OnLogExecuting = (sql, pars) =>
        {
            if (!_logger.IsEnabled(LogLevel.Information))
            {
                return;
            }

            var safeSql = SqlLogSanitizer.SanitizeSqlForLog(sql);
            var safeParameters = SqlLogSanitizer.FormatParametersForLog(pars);
            _logger.LogInformation("执行SQL: {Sql} | 参数: {Parameters}", safeSql, safeParameters);
        };

        db.Aop.OnError = exp =>
        {
            var safeSql = SqlLogSanitizer.SanitizeSqlForLog(exp.Sql);
            _logger.LogError("SQL执行错误: {Message} | SQL: {Sql}", exp.Message, safeSql);
        };
    }
}
