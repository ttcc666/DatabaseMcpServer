using System.ComponentModel;
using ModelContextProtocol.Server;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Services;

namespace DatabaseMcpServer.Tools;

/// <summary>
/// 数据库连接工具类，用于管理数据库连接。
/// </summary>
internal class DatabaseConnectionTools
{
    /// <summary>
    /// 测试数据库连接（使用环境变量中的配置）。
    /// </summary>
    /// <returns>包含连接测试结果的 JSON 字符串</returns>
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
}
