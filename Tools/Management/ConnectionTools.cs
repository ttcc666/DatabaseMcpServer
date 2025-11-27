using DatabaseMcpServer.Filters;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace DatabaseMcpServer.Tools.Management;

/// <summary>
/// 数据库连接与配置管理工具类
/// </summary>
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
        var isValid = _databaseConfig.ValidateConfiguration();
        return _databaseHelper.SerializeResult(new
        {
            success = isValid,
            configured = isValid,
            currentDatabase = _databaseConfig.GetCurrentDatabaseName(),
            databaseType = _databaseConfig.GetDatabaseType(),
            message = isValid ? "配置有效" : "配置无效,请检查 MCP 配置文件中的环境变量",
        });
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
}
