using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Services;

namespace DatabaseMcpServer.Tools;

/// <summary>
/// 数据库命令工具类，用于执行数据库的 INSERT、UPDATE、DELETE 等操作。
/// </summary>
internal class DatabaseCommandTools
{
    /// <summary>
    /// 执行 SQL 命令（INSERT、UPDATE、DELETE）（使用环境变量中的配置）。
    /// </summary>
    /// <param name="sql">要执行的 SQL 命令</param>
    /// <param name="parameters">可选的 JSON 参数</param>
    /// <returns>包含受影响行数的 JSON 字符串</returns>
    [McpServerTool]
    [Description("执行 SQL 命令 (INSERT, UPDATE, DELETE)")]
    public string ExecuteCommand(
        [Description("要执行的 SQL 命令")] string sql,
        [Description("可选的 JSON 参数")] string? parameters = null)
    {
        try
        {
            if (DatabaseHelper.DetectDangerousOperation(sql))
            {
                return DatabaseHelper.SerializeResult(new
                {
                    success = false,
                    error = "检测到危险操作。请使用特定工具进行架构操作。"
                });
            }

            using var db = DatabaseConfigService.CreateGlobalClient();
            var parsedParams = DatabaseHelper.ParseParameters(parameters);
            var affectedRows = parsedParams != null
                ? db.Ado.ExecuteCommand(sql, parsedParams)
                : db.Ado.ExecuteCommand(sql);

            return DatabaseHelper.SerializeResult(new { success = true, affectedRows });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 将数据插入到表中（使用环境变量中的配置）。
    /// </summary>
    /// <param name="tableName">表名</param>
    /// <param name="data">要插入的 JSON 数据</param>
    /// <returns>包含受影响行数的 JSON 字符串</returns>
    [McpServerTool]
    [Description("将数据插入到表中")]
    public string InsertData(
        [Description("表名")] string tableName,
        [Description("要插入的 JSON 数据")] string data)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var dataDict = JsonSerializer.Deserialize<Dictionary<string, object>>(data);
            if (dataDict == null)
                throw new ArgumentException("无效的 JSON 数据");

            var result = db.Insertable(dataDict).AS(tableName).ExecuteCommand();
            return DatabaseHelper.SerializeResult(new { success = true, affectedRows = result });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 更新表中的数据（使用环境变量中的配置）。
    /// </summary>
    /// <param name="tableName">表名</param>
    /// <param name="data">要更新的 JSON 数据</param>
    /// <param name="whereClause">WHERE 条件</param>
    /// <returns>包含受影响行数的 JSON 字符串</returns>
    [McpServerTool]
    [Description("更新表中的数据")]
    public string UpdateData(
        [Description("表名")] string tableName,
        [Description("要更新的 JSON 数据")] string data,
        [Description("WHERE 条件")] string whereClause)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var dataDict = JsonSerializer.Deserialize<Dictionary<string, object>>(data);
            if (dataDict == null)
                throw new ArgumentException("无效的 JSON 数据");

            var result = db.Updateable(dataDict).AS(tableName).Where(whereClause).ExecuteCommand();
            return DatabaseHelper.SerializeResult(new { success = true, affectedRows = result });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 从表中删除数据（使用环境变量中的配置）。
    /// </summary>
    /// <param name="tableName">表名</param>
    /// <param name="whereClause">WHERE 条件</param>
    /// <returns>包含受影响行数的 JSON 字符串</returns>
    [McpServerTool]
    [Description("从表中删除数据")]
    public string DeleteData(
        [Description("表名")] string tableName,
        [Description("WHERE 条件")] string whereClause)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var result = db.Deleteable<object>().AS(tableName).Where(whereClause).ExecuteCommand();
            return DatabaseHelper.SerializeResult(new { success = true, affectedRows = result });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 执行包含多条 SQL 命令的事务（使用环境变量中的配置）。
    /// </summary>
    /// <param name="commands">SQL 命令的 JSON 数组</param>
    /// <returns>包含事务执行结果的 JSON 字符串</returns>
    [McpServerTool]
    [Description("执行包含多条 SQL 命令的事务")]
    public string ExecuteTransaction(
        [Description("SQL 命令的 JSON 数组")] string commands)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var commandList = JsonSerializer.Deserialize<string[]>(commands);
            if (commandList == null || commandList.Length == 0)
                throw new ArgumentException("无效的命令数组");

            var result = db.Ado.UseTran(() =>
            {
                foreach (var cmd in commandList)
                {
                    if (DatabaseHelper.DetectDangerousOperation(cmd))
                        throw new InvalidOperationException("在事务中检测到危险操作");
                    db.Ado.ExecuteCommand(cmd);
                }
            });

            return DatabaseHelper.SerializeResult(new { success = result.IsSuccess, error = result.ErrorMessage });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }
}
