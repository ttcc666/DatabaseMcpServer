using DatabaseMcpServer.Filters;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;

namespace DatabaseMcpServer.Tools.Management;

/// <summary>
/// 数据库连接与配置管理工具类
/// </summary>
[McpServerToolType]
internal class ConnectionTools
{
    private readonly IDatabaseConfigService _databaseConfig;
    private readonly IDatabaseHelperService _databaseHelper;
    private readonly ILogger<ConnectionTools> _logger;

    public ConnectionTools(IDatabaseConfigService databaseConfig, IDatabaseHelperService databaseHelper, ILogger<ConnectionTools> logger)
    {
        _databaseConfig = databaseConfig;
        _databaseHelper = databaseHelper;
        _logger = logger;
    }

    [McpServerTool]
    [Description("Run SELECT 1 on the currently active connection and return success, connected, currentDatabase, and databaseType so callers can confirm the session is healthy.")]
    public string TestConnection()
    {
        _logger.LogInformation("开始测试数据库连接");
        try
        {
            using var db = _databaseConfig.CreateClient();
            var isConnected = db.Ado.GetDataTable("SELECT 1").Rows.Count > 0;

            if (!isConnected)
            {
                throw new DatabaseMcpException(DatabaseErrorCode.ConnectionFailed, "数据库连接测试失败");
            }

            _logger.LogInformation("数据库连接测试完成，结果: {IsConnected}", isConnected);

            return _databaseHelper.SerializeResult(new
            {
                success = true,
                message = "连接成功",
                connected = isConnected,
                currentDatabase = _databaseConfig.GetCurrentDatabaseName(),
                databaseType = _databaseConfig.GetDatabaseType()
            });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Create a connection using databaseName, run SELECT 1, and return success, connected, and databaseName to prove that specific entry is healthy.")]
    public string TestConnectionByName([Description("Database connection name")] string databaseName)
    {
        _logger.LogInformation("开始测试指定数据库连接: {Name}", databaseName);
        try
        {
            using var db = _databaseConfig.CreateClient(databaseName);
            var isConnected = db.Ado.GetDataTable("SELECT 1").Rows.Count > 0;

            if (!isConnected)
            {
                throw new DatabaseMcpException(DatabaseErrorCode.ConnectionFailed, $"数据库 '{databaseName}' 连接测试失败");
            }

            _logger.LogInformation("数据库 '{Name}' 连接测试完成", databaseName);

            return _databaseHelper.SerializeResult(new
            {
                success = true,
                message = $"数据库 '{databaseName}' 连接成功",
                connected = isConnected,
                databaseName
            });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Summarize DB_CONFIG_PATH or DB_CONNECTION_STRING/DB_TYPE and return the active connection name, description, database type, masked connection string, and mode metadata.")]
    public string GetDatabaseConfig()
    {
        return _databaseConfig.GetConfigurationSummary();
    }

    [McpServerTool]
    [Description("Verify whether the environment variables or config file can produce a usable connection and return success/configured/currentDatabase/databaseType/message fields describing the outcome.")]
    public string ValidateConfiguration()
    {
        try
        {
            var isValid = _databaseConfig.ValidateConfiguration();
            string? currentDatabase = null;
            string? databaseType = null;

            if (isValid)
            {
                currentDatabase = _databaseConfig.GetCurrentDatabaseName();
                databaseType = _databaseConfig.GetDatabaseType();
            }

            return _databaseHelper.SerializeResult(new
            {
                success = isValid,
                configured = isValid,
                currentDatabase,
                databaseType,
                message = isValid ? "配置有效" : "配置无效,请检查 MCP 配置文件中的环境变量"
            });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("List every configured database connection (name, type, description, default flag, current flag) so callers can choose a target.")]
    public string ListDatabases()
    {
        try
        {
            var connections = _databaseConfig.GetAllConnections();
            var currentDb = _databaseConfig.GetCurrentDatabaseName();

            var result = connections.Select(conn => new
            {
                name = conn.Name,
                dbType = conn.DbType,
                description = conn.Description,
                isDefault = conn.IsDefault,
                isCurrent = conn.Name == currentDb
            }).ToList();

            return _databaseHelper.SerializeResult(new
            {
                success = true,
                totalDatabases = result.Count,
                currentDatabase = currentDb,
                databases = result
            });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Switch the active connection to databaseName and return previousDatabase/currentDatabase; throw an error if the name does not exist.")]
    public string SwitchDatabase([Description("Database connection name to switch to")] string databaseName)
    {
        try
        {
            var previousDb = _databaseConfig.GetCurrentDatabaseName();
            var success = _databaseConfig.SwitchDatabase(databaseName);

            if (!success)
            {
                throw new DatabaseMcpException(DatabaseErrorCode.ConnectionFailed,
                    $"切换数据库失败: '{databaseName}' 不存在");
            }

            _logger.LogInformation("已从 {Previous} 切换到 {Current}", previousDb, databaseName);

            return _databaseHelper.SerializeResult(new
            {
                success = true,
                message = $"已成功切换到数据库 '{databaseName}'",
                previousDatabase = previousDb,
                currentDatabase = databaseName
            });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Return the currently active database connection name and database type so callers know the execution context.")]
    public string GetCurrentDatabase()
    {
        try
        {
            var currentDb = _databaseConfig.GetCurrentDatabaseName();

            return _databaseHelper.SerializeResult(new
            {
                success = true,
                currentDatabase = currentDb,
                databaseType = _databaseConfig.GetDatabaseType()
            });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Perform a comprehensive health check on all configured database connections, testing connectivity and response time for each.")]
    public string HealthCheck()
    {
        _logger.LogInformation("开始执行数据库连接健康检查");
        try
        {
            var connections = _databaseConfig.GetAllConnections();
            var healthResults = new List<HealthCheckResult>();

            foreach (var conn in connections)
            {
                var stopwatch = Stopwatch.StartNew();
                var checkedAt = DateTime.UtcNow;

                try
                {
                    using var db = _databaseConfig.CreateClient(conn.Name);
                    var result = db.Ado.GetDataTable("SELECT 1");

                    healthResults.Add(new HealthCheckResult
                    {
                        Name = conn.Name,
                        DbType = conn.DbType,
                        IsHealthy = result.Rows.Count > 0,
                        ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                        ErrorMessage = string.Empty,
                        CheckedAt = checkedAt
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "数据库 {Name} 健康检查失败", conn.Name);

                    healthResults.Add(new HealthCheckResult
                    {
                        Name = conn.Name,
                        DbType = conn.DbType,
                        IsHealthy = false,
                        ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                        ErrorMessage = ex.Message,
                        CheckedAt = checkedAt
                    });
                }
            }

            var totalHealthy = healthResults.Count(r => r.IsHealthy);
            var overallHealth = totalHealthy == connections.Count;

            _logger.LogInformation("健康检查完成: {Healthy}/{Total} 连接正常", totalHealthy, connections.Count);

            return _databaseHelper.SerializeResult(new
            {
                success = true,
                overallHealth,
                totalConnections = connections.Count,
                healthyConnections = totalHealthy,
                unhealthyConnections = connections.Count - totalHealthy,
                results = healthResults
            });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Test connection with automatic retry mechanism. Attempts to reconnect up to maxRetries times with exponential backoff.")]
    public async Task<string> TestConnectionWithRetry(
        [Description("Maximum number of retry attempts (default: 3)")] int maxRetries = 3,
        [Description("Initial retry delay in milliseconds (default: 1000)")] int initialDelayMs = 1000)
    {
        try
        {
            if (maxRetries < 0 || maxRetries > 10)
            {
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "maxRetries 必须在 0-10 之间");
            }

            if (initialDelayMs <= 0 || initialDelayMs > 60000)
            {
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "initialDelayMs 必须在 1-60000 之间");
            }

            _logger.LogInformation("开始测试数据库连接（带重试机制）: 最大重试次数={MaxRetries}", maxRetries);

            var attempts = 0;
            var currentDelay = initialDelayMs;
            Exception? lastException = null;

            while (attempts <= maxRetries)
            {
                try
                {
                    attempts++;
                    _logger.LogDebug("连接尝试 {Attempt}/{MaxAttempts}", attempts, maxRetries + 1);

                    using var db = _databaseConfig.CreateClient();
                    var isConnected = db.Ado.GetDataTable("SELECT 1").Rows.Count > 0;

                    if (isConnected)
                    {
                        _logger.LogInformation("数据库连接成功（第 {Attempt} 次尝试）", attempts);
                        return _databaseHelper.SerializeResult(new
                        {
                            success = true,
                            message = "连接成功",
                            connected = true,
                            attempts,
                            currentDatabase = _databaseConfig.GetCurrentDatabaseName(),
                            databaseType = _databaseConfig.GetDatabaseType()
                        });
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _logger.LogWarning(ex, "连接尝试 {Attempt} 失败", attempts);

                    if (attempts <= maxRetries)
                    {
                        _logger.LogInformation("等待 {Delay}ms 后重试...", currentDelay);
                        await Task.Delay(currentDelay);
                        currentDelay *= 2;
                    }
                }
            }

            _logger.LogError(lastException, "数据库连接失败，已达到最大重试次数 {MaxRetries}", maxRetries);
            return _databaseHelper.SerializeResult(new
            {
                success = false,
                message = $"连接失败，已尝试 {attempts} 次",
                connected = false,
                attempts,
                error = lastException?.Message ?? "未知错误"
            });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
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
