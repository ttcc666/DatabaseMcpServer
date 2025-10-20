using System.ComponentModel;
using System.Data;
using System.Text.Json;
using ModelContextProtocol.Server;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Services;
using SqlSugar;
using DbType = SqlSugar.DbType;

namespace DatabaseMcpServer.Tools.Query;

/// <summary>
/// 数据库查询工具类，支持各种查询操作
/// </summary>
internal class QueryTools
{
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
                var rows = ConvertDataTableToList(table);
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