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
    [Description("Test database connection")]
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
                databaseType = _databaseConfig.GetDatabaseType()
            });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Get current database configuration from environment variables")]
    public string GetDatabaseConfig()
    {
        return _databaseConfig.GetConfigurationSummary();
    }

    [McpServerTool]
    [Description("Validate if database configuration is correct")]
    public string ValidateConfiguration()
    {
        var isValid = _databaseConfig.ValidateConfiguration();
        return _databaseHelper.SerializeResult(new
        {
            success = isValid,
            configured = isValid,
            databaseType = _databaseConfig.GetDatabaseType(),
            message = isValid ? "配置有效" : "配置无效,请检查 MCP 配置文件中的环境变量",
        });
    }
}