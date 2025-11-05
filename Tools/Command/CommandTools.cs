using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Filters;
using DatabaseMcpServer.Models;
using ModelContextProtocol.Server;
using Microsoft.Extensions.Logging;
using SqlSugar;
using System.ComponentModel;
using System.Data;
using System.Text.Json;

namespace DatabaseMcpServer.Tools.Command;

/// <summary>
/// 数据库命令执行工具类，用于执行数据库的增删改操作、存储过程和事务
/// </summary>
internal class CommandTools
{
    private readonly IDatabaseConfigService _databaseConfig;
    private readonly IDatabaseHelperService _databaseHelper;
    private readonly ILogger<CommandTools> _logger;

    public CommandTools(IDatabaseConfigService databaseConfig, IDatabaseHelperService databaseHelper, ILogger<CommandTools> logger)
    {
        _databaseConfig = databaseConfig;
        _databaseHelper = databaseHelper;
        _logger = logger;
    }

    [McpServerTool]
    [Description("执行 SQL 命令 (INSERT, UPDATE, DELETE)")]
    public string ExecuteCommand(
        [Description("要执行的 SQL 命令")] string sql,
        [Description("可选的 JSON 参数")] string? parameters = null)
    {
        try
        {
            if (_databaseHelper.DetectDangerousOperation(sql))
            {
                throw new DatabaseMcpException(DatabaseErrorCode.DangerousOperation,
                    "检测到危险操作。请使用特定工具进行架构操作。");
            }

            using var db = _databaseConfig.CreateClient();
            var parsedParams = _databaseHelper.ParseParameters(parameters);
            var affectedRows = parsedParams != null
                ? db.Ado.ExecuteCommand(sql, parsedParams)
                : db.Ado.ExecuteCommand(sql);

            return _databaseHelper.SerializeResult(new { success = true, affectedRows });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("将数据插入到表中")]
    public string InsertData(
        [Description("表名")] string tableName,
        [Description("要插入的 JSON 数据")] string data)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var dataDict = JsonSerializer.Deserialize<Dictionary<string, object>>(data);
            if (dataDict == null)
                throw new ArgumentException("无效的 JSON 数据");

            var result = db.Insertable(dataDict).AS(tableName).ExecuteCommand();
            return _databaseHelper.SerializeResult(new { success = true, affectedRows = result });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("更新表中的数据")]
    public string UpdateData(
        [Description("表名")] string tableName,
        [Description("要更新的 JSON 数据")] string data,
        [Description("WHERE 条件")] string whereClause)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var dataDict = JsonSerializer.Deserialize<Dictionary<string, object>>(data);
            if (dataDict == null)
                throw new ArgumentException("无效的 JSON 数据");

            var result = db.Updateable(dataDict).AS(tableName).Where(whereClause).ExecuteCommand();
            return _databaseHelper.SerializeResult(new { success = true, affectedRows = result });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("从表中删除数据")]
    public string DeleteData(
        [Description("表名")] string tableName,
        [Description("WHERE 条件")] string whereClause)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var result = db.Deleteable<object>().AS(tableName).Where(whereClause).ExecuteCommand();
            return _databaseHelper.SerializeResult(new { success = true, affectedRows = result });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("执行包含多条 SQL 命令的事务")]
    public string ExecuteTransaction(
        [Description("SQL 命令的 JSON 数组")] string commands)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var commandList = JsonSerializer.Deserialize<string[]>(commands);
            if (commandList == null || commandList.Length == 0)
                throw new ArgumentException("无效的命令数组");

            var result = db.Ado.UseTran(() =>
            {
                foreach (var cmd in commandList)
                {
                    if (_databaseHelper.DetectDangerousOperation(cmd))
                        throw new InvalidOperationException("在事务中检测到危险操作");
                    db.Ado.ExecuteCommand(cmd);
                }
            });

            return _databaseHelper.SerializeResult(new { success = result.IsSuccess, error = result.ErrorMessage });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("调用存储过程（简单用法）")]
    public string CallStoredProcedure(
        [Description("存储过程名称")] string procedureName,
        [Description("存储过程参数的 JSON 对象")] string? parameters = null)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();

            if (string.IsNullOrWhiteSpace(parameters))
            {
                var result = db.Ado.UseStoredProcedure().GetDataTable(procedureName);
                var rows = ConvertDataTableToList(result);
                return _databaseHelper.SerializeResult(new
                {
                    success = true,
                    rowCount = rows.Count,
                    data = rows
                });
            }
            else
            {
                var paramsDict = JsonSerializer.Deserialize<Dictionary<string, object>>(parameters);
                if (paramsDict == null)
                    throw new ArgumentException("无效的参数 JSON");

                var result = db.Ado.UseStoredProcedure().GetDataTable(procedureName, paramsDict);
                var rows = ConvertDataTableToList(result);
                return _databaseHelper.SerializeResult(new
                {
                    success = true,
                    rowCount = rows.Count,
                    data = rows
                });
            }
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("调用带有输出参数的存储过程")]
    public string CallStoredProcedureWithOutput(
        [Description("存储过程名称")] string procedureName,
        [Description("输入参数的 JSON 对象")] string? inputParameters = null,
        [Description("输出参数名称数组的 JSON")] string? outputParameters = null)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var sugarParams = new List<SugarParameter>();

            if (!string.IsNullOrWhiteSpace(inputParameters))
            {
                var inputDict = JsonSerializer.Deserialize<Dictionary<string, object>>(inputParameters);
                if (inputDict != null)
                {
                    foreach (var kvp in inputDict)
                    {
                        sugarParams.Add(new SugarParameter(kvp.Key, kvp.Value));
                    }
                }
            }

            var outputParamNames = new List<string>();
            if (!string.IsNullOrWhiteSpace(outputParameters))
            {
                outputParamNames = JsonSerializer.Deserialize<List<string>>(outputParameters) ?? new List<string>();
                foreach (var paramName in outputParamNames)
                {
                    sugarParams.Add(new SugarParameter(paramName, null, true));
                }
            }

            var result = db.Ado.UseStoredProcedure().GetDataTable(procedureName, sugarParams.ToArray());
            var rows = ConvertDataTableToList(result);

            var outputValues = new Dictionary<string, object?>();
            foreach (var param in sugarParams.Where(p => p.Direction == System.Data.ParameterDirection.Output))
            {
                outputValues[param.ParameterName] = param.Value;
            }

            return _databaseHelper.SerializeResult(new
            {
                success = true,
                rowCount = rows.Count,
                data = rows,
                outputParameters = outputValues
            });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("执行包含 GO 语句的 SQL Server 脚本")]
    public string ExecuteCommandWithGo(
        [Description("包含 GO 语句的 SQL 脚本")] string sql)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var result = db.Ado.ExecuteCommandWithGo(sql);

            return _databaseHelper.SerializeResult(new
            {
                success = true,
                affectedRows = result
            });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("批量执行 SQL 命令（使用长连接优化性能）")]
    public string BatchExecuteCommands(
        [Description("SQL 命令数组的 JSON")] string commands,
        [Description("每个命令对应的参数对象的 JSON 数组（可选）")] string? parametersArray = null)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var commandList = JsonSerializer.Deserialize<string[]>(commands);
            if (commandList == null || commandList.Length == 0)
                throw new ArgumentException("无效的命令数组");

            List<Dictionary<string, object>>? paramsList = null;
            if (!string.IsNullOrWhiteSpace(parametersArray))
            {
                paramsList = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(parametersArray);
            }

            var results = new List<object>();

            using (db.Ado.OpenAlways())
            {
                for (int i = 0; i < commandList.Length; i++)
                {
                    try
                    {
                        var cmd = commandList[i];
                        if (_databaseHelper.DetectDangerousOperation(cmd))
                        {
                            results.Add(new { success = false, error = "检测到危险操作", commandIndex = i });
                            continue;
                        }

                        int affectedRows;
                        if (paramsList != null && i < paramsList.Count && paramsList[i] != null)
                        {
                            var sugarParams = paramsList[i].Select(p => new SugarParameter(p.Key, p.Value)).ToArray();
                            affectedRows = db.Ado.ExecuteCommand(cmd, sugarParams);
                        }
                        else
                        {
                            affectedRows = db.Ado.ExecuteCommand(cmd);
                        }

                        results.Add(new { success = true, affectedRows, commandIndex = i });
                    }
                    catch (Exception ex)
                    {
                        results.Add(new { success = false, error = ex.Message, commandIndex = i });
                    }
                }
            }

            return _databaseHelper.SerializeResult(new
            {
                success = true,
                totalCommands = commandList.Length,
                results = results
            });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    private static List<Dictionary<string, object?>> ConvertDataTableToList(DataTable dataTable)
    {
        var rows = new List<Dictionary<string, object?>>();
        foreach (DataRow row in dataTable.Rows)
        {
            var dict = new Dictionary<string, object?>();
            foreach (DataColumn col in dataTable.Columns)
            {
                dict[col.ColumnName] = row[col] == DBNull.Value ? null : row[col];
            }
            rows.Add(dict);
        }
        return rows;
    }
}