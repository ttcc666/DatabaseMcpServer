using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Tools;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using SqlSugar;
using System.ComponentModel;
using System.Text.Json;

namespace DatabaseMcpServer.Tools.Command;

/// <summary>
/// 数据库命令执行工具类，用于执行数据库的增删改操作、存储过程和事务。
/// </summary>
[McpServerToolType]
internal class CommandTools : McpToolBase
{
    public CommandTools(
        IDatabaseConfigService databaseConfig,
        IDatabaseHelperService databaseHelper,
        IJsonResultSerializer resultSerializer,
        ILogger<CommandTools> logger)
        : base(databaseConfig, databaseHelper, resultSerializer, logger)
    {
    }

    [McpServerTool]
    [Description("Execute INSERT/UPDATE/DELETE SQL after dangerous-operation detection, optionally binding JSON parameters, and return affectedRows.")]
    public string ExecuteCommand(
        [Description("SQL command to execute")] string sql,
        [Description("Optional JSON parameters")] string? parameters = null)
    {
        return WithClient(db =>
        {
            EnsureSafeSql(sql);
            var parsedParams = DatabaseHelper.ParseParameters(parameters);
            var affectedRows = parsedParams != null
                ? db.Ado.ExecuteCommand(sql, parsedParams)
                : db.Ado.ExecuteCommand(sql);

            return new { success = true, affectedRows };
        });
    }

    [McpServerTool]
    [Description("Invoke the specified stored procedure with optional JSON parameters and return the resulting rows and rowCount.")]
    public string CallStoredProcedure(
        [Description("Stored procedure name")] string procedureName,
        [Description("JSON object of stored procedure parameters")] string? parameters = null)
    {
        return WithClient(db =>
        {
            if (string.IsNullOrWhiteSpace(parameters))
            {
                var result = db.Ado.UseStoredProcedure().GetDataTable(procedureName);
                var rows = DatabaseHelper.ConvertDataTableToList(result);
                return new
                {
                    success = true,
                    rowCount = rows.Count,
                    data = rows
                };
            }

            var paramsDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(parameters);
            if (paramsDict == null)
            {
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "无效的参数 JSON");
            }

            var convertedDict = paramsDict.ToDictionary(
                kvp => kvp.Key,
                kvp => JsonElementValueConverter.ConvertToValue(kvp.Value));

            var table = db.Ado.UseStoredProcedure().GetDataTable(procedureName, convertedDict);
            var tableRows = DatabaseHelper.ConvertDataTableToList(table);

            return new
            {
                success = true,
                rowCount = tableRows.Count,
                data = tableRows
            };
        });
    }

    [McpServerTool]
    [Description("Invoke a stored procedure with JSON input parameters and a list of output parameter names; return rows plus the output parameter values.")]
    public string CallStoredProcedureWithOutput(
        [Description("Stored procedure name")] string procedureName,
        [Description("JSON object of input parameters")] string? inputParameters = null,
        [Description("JSON array of output parameter names")] string? outputParameters = null)
    {
        return WithClient(db =>
        {
            var sugarParams = new List<SugarParameter>();

            if (!string.IsNullOrWhiteSpace(inputParameters))
            {
                var inputDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(inputParameters);
                if (inputDict != null)
                {
                    foreach (var kvp in inputDict)
                    {
                        sugarParams.Add(new SugarParameter(kvp.Key, JsonElementValueConverter.ConvertToValue(kvp.Value)));
                    }
                }
            }

            var outputParamNames = new List<string>();
            if (!string.IsNullOrWhiteSpace(outputParameters))
            {
                outputParamNames = JsonSerializer.Deserialize<List<string>>(outputParameters) ?? [];
                foreach (var paramName in outputParamNames)
                {
                    sugarParams.Add(new SugarParameter(paramName, null, true));
                }
            }

            var result = db.Ado.UseStoredProcedure().GetDataTable(procedureName, sugarParams.ToArray());
            var rows = DatabaseHelper.ConvertDataTableToList(result);
            var outputValues = sugarParams
                .Where(p => p.Direction == System.Data.ParameterDirection.Output)
                .ToDictionary(p => p.ParameterName, p => p.Value);

            return new
            {
                success = true,
                rowCount = rows.Count,
                data = rows,
                outputParameters = outputValues
            };
        });
    }

    [McpServerTool]
    [Description("Execute a SQL Server script that contains GO batches, automatically splitting the script and returning total affectedRows.")]
    public string ExecuteCommandWithGo(
        [Description("SQL script containing GO statements")] string sql)
    {
        return WithClient(db =>
        {
            EnsureSafeSql(sql);
            var affectedRows = db.Ado.ExecuteCommandWithGo(sql);
            return new
            {
                success = true,
                affectedRows
            };
        });
    }

    [McpServerTool]
    [Description("Execute a JSON array of SQL commands (with optional per-command parameter dictionaries) over a single long-lived connection and return success, affectedRows, or error per command.")]
    public string BatchExecuteCommands(
        [Description("SQL commands: JSON array, single SQL string, or JSON-stringified array")] JsonElement commands,
        [Description("Optional parameters per command: JSON array/object, or JSON-stringified array/object")] JsonElement? parametersArray = null)
    {
        return WithClient(db =>
        {
            var commandList = ParseCommandList(commands);
            var paramsList = ParseParametersArray(parametersArray);
            var results = new List<object>();

            using (db.Ado.OpenAlways())
            {
                for (var i = 0; i < commandList.Length; i++)
                {
                    try
                    {
                        var command = commandList[i];
                        if (!DatabaseConfig.AllowDangerousOperations() && DatabaseHelper.DetectDangerousOperation(command))
                        {
                            results.Add(new { success = false, error = "检测到危险操作。请使用特定工具进行架构操作，或在当前连接配置中显式设置 allowDangerousOperations=true。", commandIndex = i });
                            continue;
                        }

                        var affectedRows = ExecuteSingleCommand(db, command, paramsList, i);
                        results.Add(new { success = true, affectedRows, commandIndex = i });
                    }
                    catch (Exception ex)
                    {
                        results.Add(new { success = false, error = ex.Message, commandIndex = i });
                    }
                }
            }

            return new
            {
                success = true,
                totalCommands = commandList.Length,
                results
            };
        });
    }

    private static int ExecuteSingleCommand(
        ISqlSugarClient db,
        string command,
        IReadOnlyList<Dictionary<string, JsonElement>?>? paramsList,
        int index)
    {
        if (paramsList != null && index < paramsList.Count && paramsList[index] != null)
        {
            var sugarParams = paramsList[index]!
                .Select(p => new SugarParameter(p.Key, JsonElementValueConverter.ConvertToValue(p.Value)))
                .ToArray();
            return db.Ado.ExecuteCommand(command, sugarParams);
        }

        return db.Ado.ExecuteCommand(command);
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
                        throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "commands 数组中的每一项必须是字符串 SQL");
                    }

                    var sql = item.GetString();
                    if (string.IsNullOrWhiteSpace(sql))
                    {
                        throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "commands 数组中包含空 SQL");
                    }

                    return sql;
                })
                .ToArray();

            if (commandList.Length == 0)
            {
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "命令数组不能为空");
            }

            return commandList;
        }

        if (commands.ValueKind == JsonValueKind.String)
        {
            var raw = commands.GetString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "commands 不能为空");
            }

            var trimmed = raw.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                var parsedList = JsonSerializer.Deserialize<string[]>(trimmed);
                if (parsedList == null || parsedList.Length == 0 || parsedList.Any(string.IsNullOrWhiteSpace))
                {
                    throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "无效的命令数组");
                }

                return parsedList;
            }

            return [trimmed];
        }

        throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "commands 参数必须是 SQL 字符串数组，或其 JSON 字符串表示");
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
            return value.EnumerateArray().Select(ParseSingleParameterObject).ToList();
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
                    throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "无效的 parametersArray 数组");
                }

                return parsedList;
            }

            if (trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                var single = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(trimmed);
                if (single == null)
                {
                    throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "无效的 parametersArray 对象");
                }

                return [single];
            }

            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "parametersArray 字符串必须是 JSON 对象或 JSON 数组");
        }

        throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "parametersArray 必须是对象、对象数组，或其 JSON 字符串表示");
    }

    private static Dictionary<string, JsonElement>? ParseSingleParameterObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.Object => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(element.GetRawText())
                ?? throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "无效的参数对象"),
            _ => throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "parametersArray 数组中的每一项必须是对象或 null")
        };
    }

    private void EnsureSafeSql(string sql)
    {
        if (!DatabaseConfig.AllowDangerousOperations() && DatabaseHelper.DetectDangerousOperation(sql))
        {
            throw new DatabaseMcpException(DatabaseErrorCode.DangerousOperation, "检测到危险操作。请使用特定工具进行架构操作，或在当前连接配置中显式设置 allowDangerousOperations=true。");
        }
    }
}
