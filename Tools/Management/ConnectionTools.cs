using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Filters;
using DatabaseMcpServer.Models;
using ModelContextProtocol.Server;
using Microsoft.Extensions.Logging;
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
    [Description("测试数据库连接")]
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
    [Description("从环境变量中获取当前数据库配置")]
    public string GetDatabaseConfig()
    {
        return _databaseConfig.GetConfigurationSummary();
    }

    [McpServerTool]
    [Description("验证数据库配置是否正确")]
    public string ValidateConfiguration()
    {
        return _databaseConfig.GetConfigurationSummary();
    }
}