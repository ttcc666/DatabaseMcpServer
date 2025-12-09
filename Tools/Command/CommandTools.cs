using DatabaseMcpServer.Filters;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
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
    [Description("Execute INSERT/UPDATE/DELETE SQL after dangerous-operation detection, optionally binding JSON parameters, and return affectedRows.")]
    /// <summary>
    /// 执行 DML（增删改）语句，含危险操作检测，支持 JSON 参数。
    /// </summary>
    public string ExecuteCommand(
        [Description("SQL command to execute")] string sql,
        [Description("Optional JSON parameters")] string? parameters = null)
    {
        try
        {
            EnsureSafeSql(sql);

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
    [Description("Invoke the specified stored procedure with optional JSON parameters and return the resulting rows and rowCount.")]
    /// <summary>
    /// 调用存储过程（可选参数），返回结果集与行数。
    /// </summary>
    public string CallStoredProcedure(
        [Description("Stored procedure name")] string procedureName,
        [Description("JSON object of stored procedure parameters")] string? parameters = null)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();

            if (string.IsNullOrWhiteSpace(parameters))
            {
                var result = db.Ado.UseStoredProcedure().GetDataTable(procedureName);
                var rows = _databaseHelper.ConvertDataTableToList(result);
                return _databaseHelper.SerializeResult(new
                {
                    success = true,
                    rowCount = rows.Count,
                    data = rows
                });
            }
            else
            {
                var paramsDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(parameters);
                if (paramsDict == null)
                    throw new ArgumentException("无效的参数 JSON");

                // 转换 JsonElement 为实际值
                var convertedDict = paramsDict.ToDictionary(
                    kvp => kvp.Key,
                    kvp => ConvertJsonElementToValue(kvp.Value)
                );

                var result = db.Ado.UseStoredProcedure().GetDataTable(procedureName, convertedDict);
                var rows = _databaseHelper.ConvertDataTableToList(result);
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
    [Description("Invoke a stored procedure with JSON input parameters and a list of output parameter names; return rows plus the output parameter values.")]
    /// <summary>
    /// 调用存储过程，支持输入参数与输出参数列表，返回结果集与输出值。
    /// </summary>
    public string CallStoredProcedureWithOutput(
        [Description("Stored procedure name")] string procedureName,
        [Description("JSON object of input parameters")] string? inputParameters = null,
        [Description("JSON array of output parameter names")] string? outputParameters = null)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var sugarParams = new List<SugarParameter>();

            if (!string.IsNullOrWhiteSpace(inputParameters))
            {
                var inputDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(inputParameters);
                if (inputDict != null)
                {
                    foreach (var kvp in inputDict)
                    {
                        sugarParams.Add(new SugarParameter(kvp.Key, ConvertJsonElementToValue(kvp.Value)));
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
            var rows = _databaseHelper.ConvertDataTableToList(result);

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
    [Description("Execute a SQL Server script that contains GO batches, automatically splitting the script and returning total affectedRows.")]
    /// <summary>
    /// 执行包含 GO 语句的 SQL Server 脚本，自动拆分并汇总影响行数。
    /// </summary>
    public string ExecuteCommandWithGo(
        [Description("SQL script containing GO statements")] string sql)
    {
        try
        {
            EnsureSafeSql(sql);
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
    [Description("Execute a JSON array of SQL commands (with optional per-command parameter dictionaries) over a single long-lived connection and return success, affectedRows, or error per command.")]
    /// <summary>
    /// 在单连接上批量执行 SQL 数组（可按条传参），逐条返回成功与影响行数。
    /// </summary>
    public string BatchExecuteCommands(
        [Description("JSON array of SQL commands")] string commands,
        [Description("JSON array of parameter objects for each command (optional)")] string? parametersArray = null)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var commandList = JsonSerializer.Deserialize<string[]>(commands);
            if (commandList == null || commandList.Length == 0)
                throw new ArgumentException("无效的命令数组");

            List<Dictionary<string, JsonElement>>? paramsList = null;
            if (!string.IsNullOrWhiteSpace(parametersArray))
            {
                paramsList = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(parametersArray);
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
                            var sugarParams = paramsList[i].Select(p =>
                                new SugarParameter(p.Key, ConvertJsonElementToValue(p.Value))).ToArray();
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

    private void EnsureSafeSql(string sql)
    {
        if (_databaseHelper.DetectDangerousOperation(sql))
        {
            throw new DatabaseMcpException(DatabaseErrorCode.DangerousOperation,
                "检测到危险操作。请使用特定工具进行架构操作。");
        }
    }

    /// <summary>
    /// 将 JsonElement 转换为实际的值类型。
    /// </summary>
    private static object? ConvertJsonElementToValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var longValue) ? longValue : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElementToValue).ToArray(),
            JsonValueKind.Object => JsonSerializer.Deserialize<Dictionary<string, object>>(element.GetRawText()),
            _ => element.GetRawText()
        };
    }
}
