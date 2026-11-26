using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using DatabaseMcpServer.Helpers;

namespace DatabaseMcpServer.Operations;

/// <summary>
/// 数据库连接与配置管理工具类。
/// </summary>
internal class ConnectionOperations : DatabaseOperationExecutor
{
    private readonly DatabaseConnectionProbe _probe;
    private readonly SqlSugarConnectionSettings _settings;
    public ConnectionOperations(IDatabaseConfigService databaseConfig, ILogger<ConnectionOperations> logger,
        DatabaseExecutionOptions executionOptions, DatabaseConnectionProbe probe, SqlSugarConnectionSettings settings)
        : base(databaseConfig, logger, executionOptions)
    {
        _probe = probe;
        _settings = settings;
    }

    public Task<object> GetDatabaseCapabilities(string? databaseName = null, CancellationToken cancellationToken = default)
        => ExecuteAsync(async () =>
        {
            using var reservation = DatabaseConfig.ReserveConnection(databaseName);
            using var lease = await reservation.AcquireAsync(cancellationToken);
            DatabaseAccessPolicy.EnsureAllowed(lease.Context, DatabaseToolAccess.Read, ExecutionOptions.Access);
            return DatabaseCapabilities.Describe(lease.Context, ExecutionOptions, _settings.Create(lease.Context.ToConnection()));
        }, cancellationToken);

    public Task<object> TestConnection(CancellationToken cancellationToken = default, string? databaseName = null,
        int maxRetries = 0, int initialDelayMs = 1000, int totalTimeoutSeconds = 30)
        => TestConnectionCore(databaseName, maxRetries, initialDelayMs, totalTimeoutSeconds, cancellationToken);

    public object GetDatabaseConfig()
    {
        return Execute(() => DatabaseConfig.GetConfigurationSummary());
    }

    public object ValidateConfiguration()
    {
        return Execute(() =>
        {
            var isValid = DatabaseConfig.ValidateConfiguration();
            string? currentDatabase = null;
            string? databaseType = null;

            if (isValid)
            {
                currentDatabase = DatabaseConfig.GetCurrentDatabaseName();
                databaseType = DatabaseConfig.GetDatabaseType();
            }

            return new
            {
                success = isValid,
                configured = isValid,
                currentDatabase,
                databaseType,
                message = isValid ? "配置有效" : "配置无效,请检查 MCP 配置文件中的环境变量"
            };
        });
    }

    public object ReloadDatabaseConfig()
    {
        return Execute(() => DatabaseConfig.ReloadConfiguration());
    }

    public object ListDatabases()
    {
        return Execute(() =>
        {
            var connections = DatabaseConfig.GetAllConnections();
            var currentDb = connections.Count == 0 ? null : DatabaseConfig.GetCurrentDatabaseName();
            var databases = connections
                .Select(conn => new
                {
                    name = conn.Name,
                    dbType = conn.DbType,
                    description = conn.Description,
                    isDefault = conn.IsDefault,
                    isCurrent = conn.Name == currentDb
                })
                .ToList();

            return new
            {
                success = true,
                totalDatabases = databases.Count,
                currentDatabase = currentDb,
                databases
            };
        });
    }

    public object SwitchDatabase(string databaseName)
    {
        return Execute(() =>
        {
            var previousDb = DatabaseConfig.GetCurrentDatabaseName();
            if (!DatabaseConfig.SwitchDatabase(databaseName))
            {
                throw new DatabaseMcpException(DatabaseErrorCode.ConnectionFailed, $"切换数据库失败: '{databaseName}' 不存在");
            }

            Logger.LogInformation("已从 {Previous} 切换到 {Current}", previousDb, databaseName);

            return new
            {
                success = true,
                message = $"已成功切换到数据库 '{databaseName}'",
                previousDatabase = previousDb,
                currentDatabase = databaseName
            };
        });
    }

    public object GetCurrentDatabase()
    {
        return Execute(() => new
        {
            success = true,
            currentDatabase = DatabaseConfig.GetCurrentDatabaseName(),
            databaseType = DatabaseConfig.GetDatabaseType()
        });
    }

    public Task<object> HealthCheck(CancellationToken cancellationToken = default,
        Action<int, int, string>? onProgress = null)
    {
        Logger.LogInformation("开始执行数据库连接健康检查");

        return ExecuteAsync(async () =>
        {
            var connections = DatabaseConfig.GetAllConnections();
            var healthResults = new List<HealthCheckResult>();
            var total = connections.Count;

            for (var i = 0; i < connections.Count; i++)
            {
                var connection = connections[i];
                var stopwatch = Stopwatch.StartNew();
                var checkedAt = DateTime.UtcNow;

                try
                {
                    using var reservation = DatabaseConfig.ReserveConnection(connection.Name);
                    using var lease = await reservation.AcquireAsync(cancellationToken);
                    await _probe.CheckAsync(lease.Context, cancellationToken);

                    healthResults.Add(new HealthCheckResult
                    {
                        Name = connection.Name,
                        DbType = lease.Context.DatabaseType,
                        IsHealthy = true,
                        ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                        ErrorMessage = string.Empty,
                        CheckedAt = checkedAt
                    });
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    Logger.LogWarning(ex, "数据库 {Name} 健康检查失败", connection.Name);
                    healthResults.Add(new HealthCheckResult
                    {
                        Name = connection.Name,
                        DbType = connection.DbType,
                        IsHealthy = false,
                        ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                        ErrorMessage = ex.Message,
                        CheckedAt = checkedAt
                    });
                }

                onProgress?.Invoke(i + 1, total, $"Checked {connection.Name} ({i + 1}/{total})");
            }

            var healthyConnections = healthResults.Count(item => item.IsHealthy);
            Logger.LogInformation("健康检查完成: {Healthy}/{Total} 连接正常", healthyConnections, connections.Count);

            return new
            {
                success = true,
                overallHealth = healthyConnections == connections.Count,
                totalConnections = connections.Count,
                healthyConnections,
                unhealthyConnections = connections.Count - healthyConnections,
                results = healthResults
            };
        }, cancellationToken);
    }

    private Task<object> TestConnectionCore(string? databaseName, int maxRetries, int initialDelayMs,
        int totalTimeoutSeconds, CancellationToken cancellationToken)
        => ExecuteAsync(async () =>
        {
            if (maxRetries is < 0 or > 10)
            {
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "maxRetries 必须在 0-10 之间");
            }

            if (initialDelayMs is <= 0 or > 60000)
            {
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "initialDelayMs 必须在 1-60000 之间");
            }

            if (totalTimeoutSeconds is <= 0 or > 300 || totalTimeoutSeconds > ExecutionOptions.MaxCommandTimeoutSeconds)
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "totalTimeoutSeconds 必须在 1-300 之间且符合服务端上限。");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(totalTimeoutSeconds));
            var attempts = 0;
            var currentDelay = initialDelayMs;
            try
            {
                using var snapshot = DatabaseConfig.ReserveConnection(databaseName);
                while (true)
                {
                    using var attempt = snapshot.CreateAttempt();
                    // Admission failures never enter the retry handler.
                    var lease = await attempt.AcquireAsync(deadline.Token);
                    Exception? failure = null;
                    try
                    {
                        attempts++;
                        var context = lease.Context;
                        await _probe.CheckAsync(context, deadline.Token);
                        return new
                        {
                            success = true,
                            message = "连接成功",
                            connected = true,
                            attempts,
                            currentDatabase = context.DatabaseName,
                            databaseType = context.DatabaseType
                        };
                    }
                    catch (Exception ex) when (!deadline.IsCancellationRequested && ex is not OperationCanceledException)
                    {
                        if (maxRetries == 0) throw;
                        failure = ex;
                    }
                    finally { lease.Dispose(); }
                    if (attempts > maxRetries || !DatabaseConnectionProbe.IsTransient(failure!))
                    {
                        var error = ApiResult<object>.CreateError("连接失败；请检查网络、认证和数据库权限。", DatabaseErrorCode.ConnectionFailed);
                        return new { success = false, connected = false, attempts,
                            message = $"连接失败，已尝试 {attempts} 次", errorMessage = error.ErrorMessage,
                            errorCode = error.ErrorCode, retryable = error.Retryable, phase = error.Phase, hint = error.Hint };
                    }
                    await Task.Delay(currentDelay, deadline.Token);
                    currentDelay = Math.Min(currentDelay * 2, 60000);
                }
            }
            catch (Exception) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new DatabaseMcpException(DatabaseErrorCode.CommandTimeout, "连接测试超过总截止时间。");
            }
        }, cancellationToken);

    private sealed class HealthCheckResult
    {
        public object Name { get; set; } = string.Empty;

        public object DbType { get; set; } = string.Empty;

        public bool IsHealthy { get; set; }

        public long ResponseTimeMs { get; set; }

        public object ErrorMessage { get; set; } = string.Empty;

        public DateTime CheckedAt { get; set; }
    }
}
