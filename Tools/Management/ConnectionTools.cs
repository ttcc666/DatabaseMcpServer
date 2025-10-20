using System.ComponentModel;
using ModelContextProtocol.Server;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Services;

namespace DatabaseMcpServer.Tools.Management;

/// <summary>
/// 数据库连接与配置管理工具类
/// </summary>
internal class ConnectionTools
{
    [McpServerTool]
    [Description("测试数据库连接")]
    public string TestConnection()
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var isConnected = db.Ado.GetDataTable("SELECT 1").Rows.Count > 0;

            return DatabaseHelper.SerializeResult(new
            {
                success = true,
                message = isConnected ? "连接成功" : "连接失败",
                connected = isConnected,
                databaseType = DatabaseConfigService.GetDatabaseType()
            });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    [McpServerTool]
    [Description("从环境变量中获取当前数据库配置")]
    public string GetDatabaseConfig()
    {
        return DatabaseConfigService.GetConfigurationSummary();
    }

    [McpServerTool]
    [Description("验证数据库配置是否正确")]
    public string ValidateConfiguration()
    {
        return DatabaseConfigService.GetConfigurationSummary();
    }
}