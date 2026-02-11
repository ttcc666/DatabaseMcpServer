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
    public string BatchExecuteCommands(
        [Description("SQL commands: JSON array, single SQL string, or JSON-stringified array")] JsonElement commands,
        [Description("Optional parameters per command: JSON array/object, or JSON-stringified array/object")] JsonElement? parametersArray = null)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var commandList = ParseCommandList(commands);
            var paramsList = ParseParametersArray(parametersArray);

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
                            var currentParams = paramsList[i]!;
                            var sugarParams = currentParams.Select(p =>
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

    private static string[] ParseCommandList(JsonElement commands)
    {
        if (commands.ValueKind == JsonValueKind.Array)
        {
            var commandList = commands
                .EnumerateArray()
                .Select(item =>
                {
                    if (item.ValueKind != JsonValueKind.String)
                    {
                        throw new ArgumentException("commands 数组中的每一项必须是字符串 SQL");
                    }

                    var sql = item.GetString();
                    if (string.IsNullOrWhiteSpace(sql))
                    {
                        throw new ArgumentException("commands 数组中包含空 SQL");
                    }

                    return sql;
                })
                .ToArray();

            if (commandList.Length == 0)
            {
                throw new ArgumentException("命令数组不能为空");
            }

            return commandList;
        }

        if (commands.ValueKind == JsonValueKind.String)
        {
            var raw = commands.GetString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new ArgumentException("commands 不能为空");
            }

            var trimmed = raw.Trim();

            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                var parsedList = JsonSerializer.Deserialize<string[]>(trimmed);
                if (parsedList == null || parsedList.Length == 0 || parsedList.Any(string.IsNullOrWhiteSpace))
                {
                    throw new ArgumentException("无效的命令数组");
                }

                return parsedList;
            }

            return [trimmed];
        }

        throw new ArgumentException("commands 参数必须是 SQL 字符串数组，或其 JSON 字符串表示");
    }

    private static List<Dictionary<string, JsonElement>?>? ParseParametersArray(JsonElement? parametersArray)
    {
        if (parametersArray == null ||
            parametersArray.Value.ValueKind == JsonValueKind.Null ||
            parametersArray.Value.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        var value = parametersArray.Value;

        if (value.ValueKind == JsonValueKind.Array)
        {
            return value
                .EnumerateArray()
                .Select(ParseSingleParameterObject)
                .ToList();
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            return [ParseSingleParameterObject(value)];
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var raw = value.GetString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            var trimmed = raw.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                var parsedList = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>?>>(trimmed);
                if (parsedList == null)
                {
                    throw new ArgumentException("无效的 parametersArray 数组");
                }

                return parsedList;
            }

            if (trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                var single = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(trimmed);
                if (single == null)
                {
                    throw new ArgumentException("无效的 parametersArray 对象");
                }

                return [single];
            }

            throw new ArgumentException("parametersArray 字符串必须是 JSON 对象或 JSON 数组");
        }

        throw new ArgumentException("parametersArray 必须是对象、对象数组，或其 JSON 字符串表示");
    }

    private static Dictionary<string, JsonElement>? ParseSingleParameterObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.Object => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(element.GetRawText())
                ?? throw new ArgumentException("无效的参数对象"),
            _ => throw new ArgumentException("parametersArray 数组中的每一项必须是对象或 null")
        };
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
