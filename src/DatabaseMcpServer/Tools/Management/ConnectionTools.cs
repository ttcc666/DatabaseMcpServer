using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Tools;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;
using DatabaseMcpServer.Helpers;

namespace DatabaseMcpServer.Tools.Management;

/// <summary>
/// 数据库连接与配置管理工具类。
/// </summary>
[McpServerToolType]
internal class ConnectionTools : McpToolBase
{
    private readonly DatabaseMcpServer.Services.DatabaseConnectionProbe _probe;
    public ConnectionTools(
        IDatabaseConfigService databaseConfig,
        IDatabaseHelperService databaseHelper,
        IJsonResultSerializer resultSerializer,
        ILogger<ConnectionTools> logger, DatabaseExecutionOptions executionOptions, DatabaseMcpServer.Services.DatabaseMetadataCache metadataCache,
        DatabaseMcpServer.Services.DatabaseConnectionProbe probe)
        : base(databaseConfig, databaseHelper, resultSerializer, logger, executionOptions, metadataCache)
    {
        _probe = probe;
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Inspect provider capabilities, execution budgets, effective driver/SqlSugar settings and advisory optimization options. Does not open a database connection or disclose credentials. Choose a named connection before querying.")]
    public string GetDatabaseCapabilities(
        [Description("Optional database connection name. Does not change current database.")] string? databaseName = null)
        => WithClientContext(context => DatabaseCapabilities.Describe(context, ExecutionOptions), databaseName);

    [McpServerTool(ReadOnly = true)]
    [Description("Validate the currently active connection and return success, connected, currentDatabase, and databaseType so callers can confirm the session is healthy.")]
    public Task<string> TestConnection(CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("开始测试数据库连接");

        return ExecuteAsync(async () =>
        {
            using var lease = await DatabaseConfig.AcquireClientAsync(cancellationToken);
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

    [McpServerTool(ReadOnly = true)]
    [Description("Create and validate a connection using databaseName, then return success, connected, and databaseName to prove that specific entry is healthy.")]
    public Task<string> TestConnectionByName([Description("Database connection name")] string databaseName, CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("开始测试指定数据库连接: {Name}", databaseName);

        return ExecuteAsync(async () =>
        {
            using var lease = await DatabaseConfig.AcquireClientAsync(databaseName, cancellationToken);
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

    [McpServerTool(ReadOnly = true)]
    [Description("Summarize DB_CONFIG_PATH or DB_CONNECTION_STRING/DB_TYPE and return the active connection name, description, database type, masked connection string, and mode metadata.")]
    public string GetDatabaseConfig()
    {
        return ExecuteRaw(() => DatabaseConfig.GetConfigurationSummary());
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Verify whether the environment variables or config file can produce a usable connection and return success/configured/currentDatabase/databaseType/message fields describing the outcome.")]
    public string ValidateConfiguration()
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

    [McpServerTool(Idempotent = true)]
    [Description("Reload the databases.json file from DB_CONFIG_PATH, update connection admission state, and return the applied currentDatabase plus whether the previous selection was preserved. In CLI tool mode, the preserved selection is also persisted for later invocations.")]
    public string ReloadDatabaseConfig()
    {
        return Execute(() => DatabaseConfig.ReloadConfiguration());
    }

    [McpServerTool(ReadOnly = true)]
    [Description("List every configured database connection (name, type, description, default flag, current flag) so callers can choose a target.")]
    public string ListDatabases()
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

    [McpServerTool(Idempotent = true)]
    [Description("Switch the active connection to databaseName and return previousDatabase/currentDatabase; throw an error if the name does not exist. In CLI tool mode, the selected connection is persisted per resolved config path for later invocations.")]
    public string SwitchDatabase([Description("Database connection name to switch to")] string databaseName)
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

    [McpServerTool(ReadOnly = true)]
    [Description("Return the currently active database connection name and database type so callers know the execution context. In CLI tool mode, this reflects the persisted current selection for the resolved config path.")]
    public string GetCurrentDatabase()
    {
        return Execute(() => new
        {
            success = true,
            currentDatabase = DatabaseConfig.GetCurrentDatabaseName(),
            databaseType = DatabaseConfig.GetDatabaseType()
        });
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Perform a comprehensive health check on all configured database connections, testing connectivity and response time for each.")]
    public Task<string> HealthCheck(CancellationToken cancellationToken = default)
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
                    using var lease = await DatabaseConfig.AcquireClientAsync(connection.Name, cancellationToken);
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

    [McpServerTool(ReadOnly = true)]
    [Description("Test connection with automatic retry mechanism. Attempts to reconnect up to maxRetries times with exponential backoff.")]
    public Task<string> TestConnectionWithRetry(
        [Description("Maximum number of retry attempts (default: 3)")] int maxRetries = 3,
        [Description("Initial retry delay in milliseconds (default: 1000)")] int initialDelayMs = 1000,
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
            DatabaseClientLease? lease = null;

            try
            {
                // Admission captures the target once. Busy admission is not a connection failure to retry.
                lease = await DatabaseConfig.AcquireClientAsync(cancellationToken);
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
        public string Name { get; set; } = string.Empty;

        public string DbType { get; set; } = string.Empty;

        public bool IsHealthy { get; set; }

        public long ResponseTimeMs { get; set; }

        public string ErrorMessage { get; set; } = string.Empty;

        public DateTime CheckedAt { get; set; }
    }
}
