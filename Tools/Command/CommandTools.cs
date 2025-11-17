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
    [Description("Execute SQL commands (INSERT, UPDATE, DELETE)")]
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
    [Description("Insert data into table")]
    public string InsertData(
        [Description("Table name")] string tableName,
        [Description("JSON data to insert")] string data)
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
    [Description("Update data in table")]
    public string UpdateData(
        [Description("Table name")] string tableName,
        [Description("JSON data to update")] string data,
        [Description("WHERE condition")] string whereClause)
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
    [Description("Delete data from table")]
    public string DeleteData(
        [Description("Table name")] string tableName,
        [Description("WHERE condition")] string whereClause)
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
    [Description("Execute transaction containing multiple SQL commands")]
    public string ExecuteTransaction(
        [Description("JSON array of SQL commands")] string commands)
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
                    EnsureSafeSql(cmd);
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
    [Description("Call stored procedure (simple usage)")]
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
                var paramsDict = JsonSerializer.Deserialize<Dictionary<string, object>>(parameters);
                if (paramsDict == null)
                    throw new ArgumentException("无效的参数 JSON");

                var result = db.Ado.UseStoredProcedure().GetDataTable(procedureName, paramsDict);
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
    [Description("Call stored procedure with output parameters")]
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
    [Description("Execute SQL Server script containing GO statements")]
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
    [Description("Batch execute SQL commands (optimized with long connection)")]
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

    private void EnsureSafeSql(string sql)
    {
        if (_databaseHelper.DetectDangerousOperation(sql))
        {
            throw new DatabaseMcpException(DatabaseErrorCode.DangerousOperation,
                "检测到危险操作。请使用特定工具进行架构操作。");
        }
    }
}