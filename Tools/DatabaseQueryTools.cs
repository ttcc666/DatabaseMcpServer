using System.ComponentModel;
using ModelContextProtocol.Server;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Services;

namespace DatabaseMcpServer.Tools;

/// <summary>
/// 数据库查询工具类，用于执行 SQL 查询操作。
/// </summary>
internal class DatabaseQueryTools
{
    /// <summary>
    /// 执行 SQL 查询并返回结果（使用环境变量中的配置）。
    /// </summary>
    /// <param name="sql">要执行的 SQL 查询</param>
    /// <param name="parameters">用于参数化查询的可选 JSON 参数</param>
    /// <returns>包含查询结果的 JSON 字符串</returns>
    [McpServerTool]
    [Description("执行 SQL 查询并返回结果")]
    public string ExecuteQuery(
        [Description("要执行的 SQL 查询")] string sql,
        [Description("用于参数化查询的可选 JSON 参数")] string? parameters = null)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var parsedParams = DatabaseHelper.ParseParameters(parameters);
            var dataTable = parsedParams != null
                ? db.Ado.GetDataTable(sql, parsedParams)
                : db.Ado.GetDataTable(sql);

            var rows = new List<Dictionary<string, object?>>();
            foreach (System.Data.DataRow row in dataTable.Rows)
            {
                var dict = new Dictionary<string, object?>();
                foreach (System.Data.DataColumn col in dataTable.Columns)
                {
                    dict[col.ColumnName] = row[col] == System.DBNull.Value ? null : row[col];
                }
                rows.Add(dict);
            }

            return DatabaseHelper.SerializeResult(new { success = true, rowCount = rows.Count, data = rows });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 执行不带参数的简单查询（使用环境变量中的配置）。
    /// </summary>
    /// <param name="sql">要执行的 SQL 查询</param>
    /// <returns>包含查询结果的 JSON 字符串</returns>
    [McpServerTool]
    [Description("执行不带参数的简单查询")]
    public string SimpleQuery(
        [Description("要执行的 SQL 查询")] string sql)
    {
        return ExecuteQuery(sql, null);
    }
}
