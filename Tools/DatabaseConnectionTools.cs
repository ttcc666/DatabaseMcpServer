using System.ComponentModel;
using ModelContextProtocol.Server;
using SampleMcpServer.Helpers;
using SampleMcpServer.Services;

namespace SampleMcpServer.Tools;

/// <summary>
/// 数据库连接工具类，用于管理数据库连接和元数据查询。
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

    /// <summary>
    /// 列出所有数据库（使用环境变量中的配置）。
    /// </summary>
    /// <returns>包含数据库列表的 JSON 字符串</returns>
    [McpServerTool]
    [Description("列出所有数据库")]
    public string ListDatabases()
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var databases = db.DbMaintenance.GetDataBaseList(db);

            return DatabaseHelper.SerializeResult(new { success = true, databases });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 列出当前数据库中的所有表（使用环境变量中的配置）。
    /// </summary>
    /// <returns>包含表列表的 JSON 字符串</returns>
    [McpServerTool]
    [Description("列出当前数据库中的所有表")]
    public string ListTables()
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var tables = db.DbMaintenance.GetTableInfoList(false);
            var tableList = tables.Select(t => new { name = t.Name, description = t.Description }).ToList();

            return DatabaseHelper.SerializeResult(new { success = true, tables = tableList });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 获取表结构信息（使用环境变量中的配置）。
    /// </summary>
    /// <param name="tableName">表名</param>
    /// <returns>包含表结构信息的 JSON 字符串</returns>
    [McpServerTool]
    [Description("获取表结构信息")]
    public string GetTableSchema(
        [Description("表名")] string tableName)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var columns = db.DbMaintenance.GetColumnInfosByTableName(tableName, false);
            var columnList = columns.Select(c => new
            {
                name = c.DbColumnName,
                dataType = c.DataType.ToString(),
                length = c.Length,
                isNullable = c.IsNullable,
                isPrimaryKey = c.IsPrimarykey,
                isIdentity = c.IsIdentity,
                defaultValue = c.DefaultValue,
                description = c.ColumnDescription
            }).ToList();

            return DatabaseHelper.SerializeResult(new { success = true, tableName, columns = columnList });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }
}
