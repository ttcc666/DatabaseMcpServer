using System.ComponentModel;
using System.Data;
using System.Text.Json;
using ModelContextProtocol.Server;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Services;
using SqlSugar;
using DbType = SqlSugar.DbType;

namespace DatabaseMcpServer.Tools;

/// <summary>
/// 高级 SQL 操作工具类，支持复杂查询、存储过程、多结果集等功能。
/// 基于 SqlSugar 的原生 SQL 操作，提供类似 DbHelper 的高级功能。
/// </summary>
internal class AdvancedSqlTools
{
    /// <summary>
    /// 执行 SQL 查询并返回强类型实体集合。
    /// </summary>
    /// <param name="sql">要执行的 SQL 查询</param>
    /// <param name="parameters">用于参数化查询的可选 JSON 参数</param>
    /// <returns>包含查询结果的 JSON 字符串</returns>
    [McpServerTool]
    [Description("执行 SQL 查询并返回强类型实体集合，支持复杂 SQL")]
    public string SqlQuery(
        [Description("要执行的 SQL 查询")] string sql,
        [Description("用于参数化查询的可选 JSON 参数")] string? parameters = null)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var parsedParams = DatabaseHelper.ParseParameters(parameters);

            var result = parsedParams != null
                ? db.Ado.SqlQuery<dynamic>(sql, parsedParams)
                : db.Ado.SqlQuery<dynamic>(sql);

            return DatabaseHelper.SerializeResult(new
            {
                success = true,
                rowCount = result.Count,
                data = result
            });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 执行 SQL 查询并返回单条记录。
    /// </summary>
    /// <param name="sql">要执行的 SQL 查询</param>
    /// <param name="parameters">用于参数化查询的可选 JSON 参数</param>
    /// <returns>包含单条记录的 JSON 字符串</returns>
    [McpServerTool]
    [Description("执行 SQL 查询并返回单条记录")]
    public string SqlQuerySingle(
        [Description("要执行的 SQL 查询")] string sql,
        [Description("用于参数化查询的可选 JSON 参数")] string? parameters = null)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var parsedParams = DatabaseHelper.ParseParameters(parameters);

            var result = parsedParams != null
                ? db.Ado.SqlQuery<dynamic>(sql, parsedParams).FirstOrDefault()
                : db.Ado.SqlQuery<dynamic>(sql).FirstOrDefault();

            return DatabaseHelper.SerializeResult(new
            {
                success = true,
                data = result
            });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 获取 DataReader，需要手动释放。
    /// </summary>
    /// <param name="sql">要执行的 SQL 查询</param>
    /// <param name="parameters">用于参数化查询的可选 JSON 参数</param>
    /// <returns>包含 DataReader 数据的 JSON 字符串</returns>
    [McpServerTool]
    [Description("获取 DataReader 数据（自动处理释放）")]
    public string GetDataReader(
        [Description("要执行的 SQL 查询")] string sql,
        [Description("用于参数化查询的可选 JSON 参数")] string? parameters = null)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var parsedParams = DatabaseHelper.ParseParameters(parameters);

            var rows = new List<Dictionary<string, object?>>();

            using var reader = parsedParams != null
                ? db.Ado.GetDataReader(sql, parsedParams)
                : db.Ado.GetDataReader(sql);

            while (reader.Read())
            {
                var dict = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    dict[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                rows.Add(dict);
            }

            return DatabaseHelper.SerializeResult(new
            {
                success = true,
                rowCount = rows.Count,
                data = rows
            });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 获取多个结果集（DataSet）。
    /// </summary>
    /// <param name="sql">要执行的 SQL 查询（可包含多个查询语句）</param>
    /// <param name="parameters">用于参数化查询的可选 JSON 参数</param>
    /// <returns>包含多个结果集的 JSON 字符串</returns>
    [McpServerTool]
    [Description("获取多个结果集，支持一次执行多个查询")]
    public string GetDataSetAll(
        [Description("要执行的 SQL 查询（可包含多个查询语句，用分号分隔）")] string sql,
        [Description("用于参数化查询的可选 JSON 参数")] string? parameters = null)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var parsedParams = DatabaseHelper.ParseParameters(parameters);

            var dataSet = parsedParams != null
                ? db.Ado.GetDataSetAll(sql, parsedParams)
                : db.Ado.GetDataSetAll(sql);

            var resultSets = new List<object>();

            foreach (DataTable table in dataSet.Tables)
            {
                var rows = new List<Dictionary<string, object?>>();
                foreach (DataRow row in table.Rows)
                {
                    var dict = new Dictionary<string, object?>();
                    foreach (DataColumn col in table.Columns)
                    {
                        dict[col.ColumnName] = row[col] == DBNull.Value ? null : row[col];
                    }
                    rows.Add(dict);
                }
                resultSets.Add(new { rowCount = rows.Count, data = rows });
            }

            return DatabaseHelper.SerializeResult(new
            {
                success = true,
                resultSetCount = resultSets.Count,
                resultSets = resultSets
            });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 获取首行首列的值（标量值）。
    /// </summary>
    /// <param name="sql">要执行的 SQL 查询</param>
    /// <param name="parameters">用于参数化查询的可选 JSON 参数</param>
    /// <returns>包含标量值的 JSON 字符串</returns>
    [McpServerTool]
    [Description("获取首行首列的值（标量值）")]
    public string GetScalar(
        [Description("要执行的 SQL 查询")] string sql,
        [Description("用于参数化查询的可选 JSON 参数")] string? parameters = null)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var parsedParams = DatabaseHelper.ParseParameters(parameters);

            var result = parsedParams != null
                ? db.Ado.GetScalar(sql, parsedParams)
                : db.Ado.GetScalar(sql);

            return DatabaseHelper.SerializeResult(new
            {
                success = true,
                value = result
            });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 获取首行首列的字符串值。
    /// </summary>
    /// <param name="sql">要执行的 SQL 查询</param>
    /// <param name="parameters">用于参数化查询的可选 JSON 参数</param>
    /// <returns>包含字符串值的 JSON 字符串</returns>
    [McpServerTool]
    [Description("获取首行首列的字符串值")]
    public string GetString(
        [Description("要执行的 SQL 查询")] string sql,
        [Description("用于参数化查询的可选 JSON 参数")] string? parameters = null)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var parsedParams = DatabaseHelper.ParseParameters(parameters);

            var result = parsedParams != null
                ? db.Ado.GetString(sql, parsedParams)
                : db.Ado.GetString(sql);

            return DatabaseHelper.SerializeResult(new
            {
                success = true,
                value = result
            });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 获取首行首列的整数值。
    /// </summary>
    /// <param name="sql">要执行的 SQL 查询</param>
    /// <param name="parameters">用于参数化查询的可选 JSON 参数</param>
    /// <returns>包含整数值的 JSON 字符串</returns>
    [McpServerTool]
    [Description("获取首行首列的整数值")]
    public string GetInt(
        [Description("要执行的 SQL 查询")] string sql,
        [Description("用于参数化查询的可选 JSON 参数")] string? parameters = null)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var parsedParams = DatabaseHelper.ParseParameters(parameters);

            var result = parsedParams != null
                ? db.Ado.GetInt(sql, parsedParams)
                : db.Ado.GetInt(sql);

            return DatabaseHelper.SerializeResult(new
            {
                success = true,
                value = result
            });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 获取首行首列的长整数值。
    /// </summary>
    /// <param name="sql">要执行的 SQL 查询</param>
    /// <param name="parameters">用于参数化查询的可选 JSON 参数</param>
    /// <returns>包含长整数值的 JSON 字符串</returns>
    [McpServerTool]
    [Description("获取首行首列的长整数值")]
    public string GetLong(
        [Description("要执行的 SQL 查询")] string sql,
        [Description("用于参数化查询的可选 JSON 参数")] string? parameters = null)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var parsedParams = DatabaseHelper.ParseParameters(parameters);

            var result = parsedParams != null
                ? db.Ado.GetLong(sql, parsedParams)
                : db.Ado.GetLong(sql);

            return DatabaseHelper.SerializeResult(new
            {
                success = true,
                value = result
            });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 获取首行首列的双精度浮点数值。
    /// </summary>
    /// <param name="sql">要执行的 SQL 查询</param>
    /// <param name="parameters">用于参数化查询的可选 JSON 参数</param>
    /// <returns>包含双精度浮点数值的 JSON 字符串</returns>
    [McpServerTool]
    [Description("获取首行首列的双精度浮点数值")]
    public string GetDouble(
        [Description("要执行的 SQL 查询")] string sql,
        [Description("用于参数化查询的可选 JSON 参数")] string? parameters = null)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var parsedParams = DatabaseHelper.ParseParameters(parameters);

            var result = parsedParams != null
                ? db.Ado.GetDouble(sql, parsedParams)
                : db.Ado.GetDouble(sql);

            return DatabaseHelper.SerializeResult(new
            {
                success = true,
                value = result
            });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 获取首行首列的十进制数值。
    /// </summary>
    /// <param name="sql">要执行的 SQL 查询</param>
    /// <param name="parameters">用于参数化查询的可选 JSON 参数</param>
    /// <returns>包含十进制数值的 JSON 字符串</returns>
    [McpServerTool]
    [Description("获取首行首列的十进制数值")]
    public string GetDecimal(
        [Description("要执行的 SQL 查询")] string sql,
        [Description("用于参数化查询的可选 JSON 参数")] string? parameters = null)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var parsedParams = DatabaseHelper.ParseParameters(parameters);

            var result = parsedParams != null
                ? db.Ado.GetDecimal(sql, parsedParams)
                : db.Ado.GetDecimal(sql);

            return DatabaseHelper.SerializeResult(new
            {
                success = true,
                value = result
            });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 获取首行首列的日期时间值。
    /// </summary>
    /// <param name="sql">要执行的 SQL 查询</param>
    /// <param name="parameters">用于参数化查询的可选 JSON 参数</param>
    /// <returns>包含日期时间值的 JSON 字符串</returns>
    [McpServerTool]
    [Description("获取首行首列的日期时间值")]
    public string GetDateTime(
        [Description("要执行的 SQL 查询")] string sql,
        [Description("用于参数化查询的可选 JSON 参数")] string? parameters = null)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var parsedParams = DatabaseHelper.ParseParameters(parameters);

            var result = parsedParams != null
                ? db.Ado.GetDateTime(sql, parsedParams)
                : db.Ado.GetDateTime(sql);

            return DatabaseHelper.SerializeResult(new
            {
                success = true,
                value = result
            });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 调用存储过程（简单用法）。
    /// </summary>
    /// <param name="procedureName">存储过程名称</param>
    /// <param name="parameters">存储过程参数的 JSON 对象</param>
    /// <returns>包含存储过程执行结果的 JSON 字符串</returns>
    [McpServerTool]
    [Description("调用存储过程（简单用法）")]
    public string CallStoredProcedure(
        [Description("存储过程名称")] string procedureName,
        [Description("存储过程参数的 JSON 对象")] string? parameters = null)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();

            if (string.IsNullOrWhiteSpace(parameters))
            {
                var result = db.Ado.UseStoredProcedure().GetDataTable(procedureName);
                var rows = ConvertDataTableToList(result);
                return DatabaseHelper.SerializeResult(new
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
                return DatabaseHelper.SerializeResult(new
                {
                    success = true,
                    rowCount = rows.Count,
                    data = rows
                });
            }
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 调用带有输出参数的存储过程。
    /// </summary>
    /// <param name="procedureName">存储过程名称</param>
    /// <param name="inputParameters">输入参数的 JSON 对象</param>
    /// <param name="outputParameters">输出参数名称数组的 JSON</param>
    /// <returns>包含存储过程执行结果和输出参数值的 JSON 字符串</returns>
    [McpServerTool]
    [Description("调用带有输出参数的存储过程")]
    public string CallStoredProcedureWithOutput(
        [Description("存储过程名称")] string procedureName,
        [Description("输入参数的 JSON 对象")] string? inputParameters = null,
        [Description("输出参数名称数组的 JSON")] string? outputParameters = null)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var sugarParams = new List<SugarParameter>();

            // 处理输入参数
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

            // 处理输出参数
            var outputParamNames = new List<string>();
            if (!string.IsNullOrWhiteSpace(outputParameters))
            {
                outputParamNames = JsonSerializer.Deserialize<List<string>>(outputParameters) ?? new List<string>();
                foreach (var paramName in outputParamNames)
                {
                    sugarParams.Add(new SugarParameter(paramName, null, true)); // true 表示输出参数
                }
            }

            var result = db.Ado.UseStoredProcedure().GetDataTable(procedureName, sugarParams.ToArray());
            var rows = ConvertDataTableToList(result);

            // 获取输出参数值
            var outputValues = new Dictionary<string, object?>();
            foreach (var param in sugarParams.Where(p => p.Direction == System.Data.ParameterDirection.Output))
            {
                outputValues[param.ParameterName] = param.Value;
            }

            return DatabaseHelper.SerializeResult(new
            {
                success = true,
                rowCount = rows.Count,
                data = rows,
                outputParameters = outputValues
            });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 执行包含 GO 语句的 SQL Server 脚本。
    /// </summary>
    /// <param name="sql">包含 GO 语句的 SQL 脚本</param>
    /// <returns>包含执行结果的 JSON 字符串</returns>
    [McpServerTool]
    [Description("执行包含 GO 语句的 SQL Server 脚本")]
    public string ExecuteCommandWithGo(
        [Description("包含 GO 语句的 SQL 脚本")] string sql)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var result = db.Ado.ExecuteCommandWithGo(sql);

            return DatabaseHelper.SerializeResult(new
            {
                success = true,
                affectedRows = result
            });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 执行查询并返回两个结果集（等同于 Dapper 的 QueryMultiple）。
    /// </summary>
    /// <param name="sql">包含两个查询的 SQL 语句（用分号分隔）</param>
    /// <param name="parameters">用于参数化查询的可选 JSON 参数</param>
    /// <returns>包含两个结果集的 JSON 字符串</returns>
    [McpServerTool]
    [Description("执行查询并返回两个结果集")]
    public string SqlQueryMultiple(
        [Description("包含两个查询的 SQL 语句（用分号分隔）")] string sql,
        [Description("用于参数化查询的可选 JSON 参数")] string? parameters = null)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var parsedParams = DatabaseHelper.ParseParameters(parameters);

            // 注意：对于 SQLite，可能需要设置 IsClearParameters = false
            if (db.CurrentConnectionConfig.DbType == DbType.Sqlite)
            {
                db.Ado.IsClearParameters = false;
            }

            var dataSet = parsedParams != null
                ? db.Ado.GetDataSetAll(sql, parsedParams)
                : db.Ado.GetDataSetAll(sql);

            if (dataSet.Tables.Count < 2)
            {
                throw new InvalidOperationException("SQL 语句必须返回至少两个结果集");
            }

            var firstResultSet = ConvertDataTableToList(dataSet.Tables[0]);
            var secondResultSet = ConvertDataTableToList(dataSet.Tables[1]);

            return DatabaseHelper.SerializeResult(new
            {
                success = true,
                firstResultSet = new { rowCount = firstResultSet.Count, data = firstResultSet },
                secondResultSet = new { rowCount = secondResultSet.Count, data = secondResultSet }
            });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 批量执行 SQL 命令（使用长连接）。
    /// </summary>
    /// <param name="commands">SQL 命令数组的 JSON</param>
    /// <param name="parametersArray">每个命令对应的参数对象的 JSON 数组（可选）</param>
    /// <returns>包含批量执行结果的 JSON 字符串</returns>
    [McpServerTool]
    [Description("批量执行 SQL 命令（使用长连接优化性能）")]
    public string BatchExecuteCommands(
        [Description("SQL 命令数组的 JSON")] string commands,
        [Description("每个命令对应的参数对象的 JSON 数组（可选）")] string? parametersArray = null)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var commandList = JsonSerializer.Deserialize<string[]>(commands);
            if (commandList == null || commandList.Length == 0)
                throw new ArgumentException("无效的命令数组");

            List<Dictionary<string, object>>? paramsList = null;
            if (!string.IsNullOrWhiteSpace(parametersArray))
            {
                paramsList = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(parametersArray);
            }

            var results = new List<object>();

            using (db.Ado.OpenAlways()) // 开启长连接
            {
                for (int i = 0; i < commandList.Length; i++)
                {
                    try
                    {
                        var cmd = commandList[i];
                        if (DatabaseHelper.DetectDangerousOperation(cmd))
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

            return DatabaseHelper.SerializeResult(new
            {
                success = true,
                totalCommands = commandList.Length,
                results = results
            });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 处理 IN 参数查询。
    /// </summary>
    /// <param name="sql">包含 IN 参数的 SQL 查询</param>
    /// <param name="inParameterName">IN 参数名称（如 "ids"）</param>
    /// <param name="inValues">IN 参数值数组的 JSON</param>
    /// <param name="otherParameters">其他参数的 JSON（可选）</param>
    /// <returns>包含查询结果的 JSON 字符串</returns>
    [McpServerTool]
    [Description("处理 IN 参数查询，支持数组参数")]
    public string SqlQueryWithInParameter(
        [Description("包含 IN 参数的 SQL 查询（如：select * from [order] where id in (@ids)）")] string sql,
        [Description("IN 参数名称（如 \"ids\"）")] string inParameterName,
        [Description("IN 参数值数组的 JSON（如：[1,2,3]）")] string inValues,
        [Description("其他参数的 JSON（可选）")] string? otherParameters = null)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();

            var inArray = JsonSerializer.Deserialize<object[]>(inValues);
            if (inArray == null)
                throw new ArgumentException("无效的 IN 参数值数组");

            var sugarParams = new List<SugarParameter>
            {
                new SugarParameter($"@{inParameterName.TrimStart('@')}", inArray)
            };

            // 添加其他参数
            if (!string.IsNullOrWhiteSpace(otherParameters))
            {
                var otherParams = DatabaseHelper.ParseParameters(otherParameters);
                if (otherParams != null)
                {
                    sugarParams.AddRange(otherParams);
                }
            }

            var result = db.Ado.SqlQuery<dynamic>(sql, sugarParams.ToArray());

            return DatabaseHelper.SerializeResult(new
            {
                success = true,
                rowCount = result.Count,
                data = result
            });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 将 DataTable 转换为字典列表的辅助方法。
    /// </summary>
    /// <param name="dataTable">要转换的 DataTable</param>
    /// <returns>字典列表</returns>
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
