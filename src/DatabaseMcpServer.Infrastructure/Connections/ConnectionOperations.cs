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

    public object GetDatabaseCapabilities(
        string? databaseName = null)
        => Execute(() =>
        {
            using var reservation = DatabaseConfig.ReserveConnection(databaseName);
            using var lease = reservation.Acquire();
            DatabaseAccessPolicy.EnsureAllowed(lease.Context, DatabaseToolAccess.Read, ExecutionOptions.Access);
            return DatabaseCapabilities.Describe(lease.Context, ExecutionOptions, _settings.Create(lease.Context.ToConnection()));
        });

    public Task<object> TestConnection(CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("开始测试数据库连接");

        return ExecuteAsync(async () =>
        {
            using var reservation = DatabaseConfig.ReserveConnection();
            using var lease = await reservation.AcquireAsync(cancellationToken);
            var context = lease.Context;
            await _probe.CheckAsync(context, cancellationToken);

            Logger.LogInformation("数据库连接测试完成");

            return new
            {
                success = true,
                message = "连接成功",
                connected = true,
                currentDatabase = context.DatabaseName,
                databaseType = context.DatabaseType
            };
        }, cancellationToken);
    }

    public Task<object> TestConnectionByName(string databaseName, CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("开始测试指定数据库连接: {Name}", databaseName);

        return ExecuteAsync(async () =>
        {
            using var reservation = DatabaseConfig.ReserveConnection(databaseName);
            using var lease = await reservation.AcquireAsync(cancellationToken);
            await _probe.CheckAsync(lease.Context, cancellationToken);

            Logger.LogInformation("数据库 '{Name}' 连接测试完成", databaseName);

            return new
            {
                success = true,
                message = $"数据库 '{databaseName}' 连接成功",
                connected = true,
                databaseName
            };
        }, cancellationToken);
    }

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
            var currentDb = DatabaseConfig.GetCurrentDatabaseName();
            var databases = DatabaseConfig
                .GetAllConnections()
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

    public Task<object> HealthCheck(CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("开始执行数据库连接健康检查");

        return ExecuteAsync(async () =>
        {
            var connections = DatabaseConfig.GetAllConnections();
            var healthResults = new List<HealthCheckResult>();

            foreach (var connection in connections)
            {
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

    public Task<object> TestConnectionWithRetry(
        int maxRetries = 3,
        int initialDelayMs = 1000,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () =>
        {
            if (maxRetries is < 0 or > 10)
            {
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "maxRetries 必须在 0-10 之间");
            }

            if (initialDelayMs is <= 0 or > 60000)
            {
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "initialDelayMs 必须在 1-60000 之间");
            }

            Logger.LogInformation("开始测试数据库连接（带重试机制）: 最大重试次数={MaxRetries}", maxRetries);

            var attempts = 0;
            var currentDelay = initialDelayMs;
            Exception? lastException = null;
            DatabaseOperationLease? lease = null;

            try
            {
                // Admission captures the target once. Busy admission is not a connection failure to retry.
                using var reservation = DatabaseConfig.ReserveConnection();
                lease = await reservation.AcquireAsync(cancellationToken);
                while (attempts <= maxRetries)
                {
                    try
                    {
                        attempts++;
                        Logger.LogDebug("连接尝试 {Attempt}/{MaxAttempts}", attempts, maxRetries + 1);

                        var context = lease.Context;
                        await _probe.CheckAsync(context, cancellationToken);

                        Logger.LogInformation("数据库连接成功（第 {Attempt} 次尝试）", attempts);
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
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is not OperationCanceledException)
                    {
                        lastException = ex;
                        Logger.LogWarning(ex, "连接尝试 {Attempt} 失败", attempts);

                        if (attempts <= maxRetries)
                        {
                            Logger.LogInformation("等待 {Delay}ms 后重试...", currentDelay);
                            await Task.Delay(currentDelay, cancellationToken);
                            currentDelay *= 2;
                        }
                    }
                }

                Logger.LogError(lastException, "数据库连接失败，已达到最大重试次数 {MaxRetries}", maxRetries);
                return new
                {
                    success = false,
                    message = $"连接失败，已尝试 {attempts} 次",
                    connected = false,
                    attempts,
                    error = lastException?.Message ?? "未知错误"
                };
            }
            finally
            {
                lease?.Dispose();
            }
        }, cancellationToken);
    }

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
