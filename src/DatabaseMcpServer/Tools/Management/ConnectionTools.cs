using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Tools;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;

namespace DatabaseMcpServer.Tools.Management;

/// <summary>
/// 数据库连接与配置管理工具类。
/// </summary>
[McpServerToolType]
internal class ConnectionTools : McpToolBase
{
    public ConnectionTools(
        IDatabaseConfigService databaseConfig,
        IDatabaseHelperService databaseHelper,
        IJsonResultSerializer resultSerializer,
        ILogger<ConnectionTools> logger)
        : base(databaseConfig, databaseHelper, resultSerializer, logger)
    {
    }

    [McpServerTool]
    [Description("Run SELECT 1 on the currently active connection and return success, connected, currentDatabase, and databaseType so callers can confirm the session is healthy.")]
    public string TestConnection()
    {
        Logger.LogInformation("开始测试数据库连接");

        return WithClient(db =>
        {
            var isConnected = db.Ado.GetDataTable("SELECT 1").Rows.Count > 0;
            if (!isConnected)
            {
                throw new DatabaseMcpException(DatabaseErrorCode.ConnectionFailed, "数据库连接测试失败");
            }

            Logger.LogInformation("数据库连接测试完成，结果: {IsConnected}", isConnected);

            return new
            {
                success = true,
                message = "连接成功",
                connected = true,
                currentDatabase = DatabaseConfig.GetCurrentDatabaseName(),
                databaseType = DatabaseConfig.GetDatabaseType()
            };
        });
    }

    [McpServerTool]
    [Description("Create a connection using databaseName, run SELECT 1, and return success, connected, and databaseName to prove that specific entry is healthy.")]
    public string TestConnectionByName([Description("Database connection name")] string databaseName)
    {
        Logger.LogInformation("开始测试指定数据库连接: {Name}", databaseName);

        return WithNamedClient(databaseName, db =>
        {
            var isConnected = db.Ado.GetDataTable("SELECT 1").Rows.Count > 0;
            if (!isConnected)
            {
                throw new DatabaseMcpException(DatabaseErrorCode.ConnectionFailed, $"数据库 '{databaseName}' 连接测试失败");
            }

            Logger.LogInformation("数据库 '{Name}' 连接测试完成", databaseName);

            return new
            {
                success = true,
                message = $"数据库 '{databaseName}' 连接成功",
                connected = true,
                databaseName
            };
        });
    }

    [McpServerTool]
    [Description("Summarize DB_CONFIG_PATH or DB_CONNECTION_STRING/DB_TYPE and return the active connection name, description, database type, masked connection string, and mode metadata.")]
    public string GetDatabaseConfig()
    {
        return ExecuteRaw(() => DatabaseConfig.GetConfigurationSummary());
    }

    [McpServerTool]
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

    [McpServerTool]
    [Description("Reload the databases.json file from DB_CONFIG_PATH, refresh cached clients, and return the applied currentDatabase plus whether the previous selection was preserved. In CLI tool mode, the preserved selection is also persisted for later invocations.")]
    public string ReloadDatabaseConfig()
    {
        return Execute(() => DatabaseConfig.ReloadConfiguration());
    }

    [McpServerTool]
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

    [McpServerTool]
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

    [McpServerTool]
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

    [McpServerTool]
    [Description("Perform a comprehensive health check on all configured database connections, testing connectivity and response time for each.")]
    public string HealthCheck()
    {
        Logger.LogInformation("开始执行数据库连接健康检查");

        return Execute(() =>
        {
            var connections = DatabaseConfig.GetAllConnections();
            var healthResults = new List<HealthCheckResult>();

            foreach (var connection in connections)
            {
                var stopwatch = Stopwatch.StartNew();
                var checkedAt = DateTime.UtcNow;

                try
                {
                    var db = DatabaseConfig.CreateClient(connection.Name);
                    var result = db.Ado.GetDataTable("SELECT 1");

                    healthResults.Add(new HealthCheckResult
                    {
                        Name = connection.Name,
                        DbType = connection.DbType,
                        IsHealthy = result.Rows.Count > 0,
                        ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                        ErrorMessage = string.Empty,
                        CheckedAt = checkedAt
                    });
                }
                catch (Exception ex)
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
        });
    }

    [McpServerTool]
    [Description("Test connection with automatic retry mechanism. Attempts to reconnect up to maxRetries times with exponential backoff.")]
    public Task<string> TestConnectionWithRetry(
        [Description("Maximum number of retry attempts (default: 3)")] int maxRetries = 3,
        [Description("Initial retry delay in milliseconds (default: 1000)")] int initialDelayMs = 1000)
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

            while (attempts <= maxRetries)
            {
                try
                {
                    attempts++;
                    Logger.LogDebug("连接尝试 {Attempt}/{MaxAttempts}", attempts, maxRetries + 1);

                    var db = DatabaseConfig.CreateClient();
                    var isConnected = db.Ado.GetDataTable("SELECT 1").Rows.Count > 0;
                    if (isConnected)
                    {
                        Logger.LogInformation("数据库连接成功（第 {Attempt} 次尝试）", attempts);
                        return new
                        {
                            success = true,
                            message = "连接成功",
                            connected = true,
                            attempts,
                            currentDatabase = DatabaseConfig.GetCurrentDatabaseName(),
                            databaseType = DatabaseConfig.GetDatabaseType()
                        };
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    Logger.LogWarning(ex, "连接尝试 {Attempt} 失败", attempts);

                    if (attempts <= maxRetries)
                    {
                        Logger.LogInformation("等待 {Delay}ms 后重试...", currentDelay);
                        await Task.Delay(currentDelay);
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
        });
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
