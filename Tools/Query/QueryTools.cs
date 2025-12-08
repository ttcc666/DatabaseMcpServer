using DatabaseMcpServer.Filters;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using SqlSugar;
using System.ComponentModel;
using System.Data;
using System.Text.Json;
using DbType = SqlSugar.DbType;

namespace DatabaseMcpServer.Tools.Query;

/// <summary>
/// 数据库查询工具类，支持各种查询操作
/// </summary>
internal class QueryTools
{
    private readonly IDatabaseConfigService _databaseConfig;
    private readonly IDatabaseHelperService _databaseHelper;
    private readonly ILogger<QueryTools> _logger;

    public QueryTools(IDatabaseConfigService databaseConfig, IDatabaseHelperService databaseHelper, ILogger<QueryTools> logger)
    {
        _databaseConfig = databaseConfig;
        _databaseHelper = databaseHelper;
        _logger = logger;
    }

    [McpServerTool]
    [Description("Execute a read-only SQL statement with dangerous-operation detection, optionally binding JSON parameters, and return rowCount plus data.")]
    public string SqlQuery(
        [Description("SQL query to execute")] string sql,
        [Description("Optional JSON parameters for parameterized queries")] string? parameters = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sql))
            {
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "SQL 查询不能为空");
            }
            EnsureSafeSql(sql);

            using var db = _databaseConfig.CreateClient();
            var parsedParams = _databaseHelper.ParseParameters(parameters);

            var result = parsedParams != null
                ? db.Ado.SqlQuery<dynamic>(sql, parsedParams)
                : db.Ado.SqlQuery<dynamic>(sql);

            return _databaseHelper.SerializeResult(new
            {
                success = true,
                rowCount = result.Count,
                data = result
            });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Execute a read-only SQL statement and return only the first row (or null) with the same optional JSON parameters.")]
    public string SqlQuerySingle(
        [Description("SQL query to execute")] string sql,
        [Description("Optional JSON parameters for parameterized queries")] string? parameters = null)
    {
        try
        {
            EnsureSafeSql(sql);
            using var db = _databaseConfig.CreateClient();
            var parsedParams = _databaseHelper.ParseParameters(parameters);

            var result = parsedParams != null
                ? db.Ado.SqlQuery<dynamic>(sql, parsedParams).FirstOrDefault()
                : db.Ado.SqlQuery<dynamic>(sql).FirstOrDefault();

            return _databaseHelper.SerializeResult(new
            {
                success = true,
                data = result
            });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Execute SQL, stream the results through a DataReader, convert each row to a dictionary, and return the full list after disposing the reader.")]
    public string GetDataReader(
        [Description("SQL query to execute")] string sql,
        [Description("Optional JSON parameters for parameterized queries")] string? parameters = null)
    {
        try
        {
            EnsureSafeSql(sql);
            using var db = _databaseConfig.CreateClient();
            var parsedParams = _databaseHelper.ParseParameters(parameters);

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

            return _databaseHelper.SerializeResult(new
            {
                success = true,
                rowCount = rows.Count,
                data = rows
            });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Execute SQL that may contain multiple SELECT statements and return each result set with its rowCount inside resultSets.")]
    public string GetDataSetAll(
        [Description("SQL query to execute (can contain multiple query statements separated by semicolons)")] string sql,
        [Description("Optional JSON parameters for parameterized queries")] string? parameters = null)
    {
        try
        {
            EnsureSafeSql(sql);
            using var db = _databaseConfig.CreateClient();
            var parsedParams = _databaseHelper.ParseParameters(parameters);

            var dataSet = parsedParams != null
                ? db.Ado.GetDataSetAll(sql, parsedParams)
                : db.Ado.GetDataSetAll(sql);

            var resultSets = new List<object>();

            foreach (DataTable table in dataSet.Tables)
            {
                var rows = _databaseHelper.ConvertDataTableToList(table);
                resultSets.Add(new { rowCount = rows.Count, data = rows });
            }

            return _databaseHelper.SerializeResult(new
            {
                success = true,
                resultSetCount = resultSets.Count,
                resultSets = resultSets
            });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Return the first-row, first-column value from the SQL statement—ideal for COUNT/SUM scalar queries.")]
    public string GetScalar(
        [Description("SQL query to execute")] string sql,
        [Description("Optional JSON parameters for parameterized queries")] string? parameters = null)
    {
        try
        {
            EnsureSafeSql(sql);
            using var db = _databaseConfig.CreateClient();
            var parsedParams = _databaseHelper.ParseParameters(parameters);

            var result = parsedParams != null
                ? db.Ado.GetScalar(sql, parsedParams)
                : db.Ado.GetScalar(sql);

            return _databaseHelper.SerializeResult(new
            {
                success = true,
                value = result
            });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Return the first-row, first-column value as a string.")]
    public string GetString(
        [Description("SQL query to execute")] string sql,
        [Description("Optional JSON parameters for parameterized queries")] string? parameters = null)
    {
        try
        {
            EnsureSafeSql(sql);
            using var db = _databaseConfig.CreateClient();
            var parsedParams = _databaseHelper.ParseParameters(parameters);

            var result = parsedParams != null
                ? db.Ado.GetString(sql, parsedParams)
                : db.Ado.GetString(sql);

            return _databaseHelper.SerializeResult(new
            {
                success = true,
                value = result
            });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Return the first-row, first-column value converted to an integer (throws if conversion fails).")]
    public string GetInt(
        [Description("SQL query to execute")] string sql,
        [Description("Optional JSON parameters for parameterized queries")] string? parameters = null)
    {
        try
        {
            EnsureSafeSql(sql);
            using var db = _databaseConfig.CreateClient();
            var parsedParams = _databaseHelper.ParseParameters(parameters);

            var result = parsedParams != null
                ? db.Ado.GetInt(sql, parsedParams)
                : db.Ado.GetInt(sql);

            return _databaseHelper.SerializeResult(new
            {
                success = true,
                value = result
            });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Return the first-row, first-column value converted to a long integer.")]
    public string GetLong(
        [Description("SQL query to execute")] string sql,
        [Description("Optional JSON parameters for parameterized queries")] string? parameters = null)
    {
        try
        {
            EnsureSafeSql(sql);
            using var db = _databaseConfig.CreateClient();
            var parsedParams = _databaseHelper.ParseParameters(parameters);

            var result = parsedParams != null
                ? db.Ado.GetLong(sql, parsedParams)
                : db.Ado.GetLong(sql);

            return _databaseHelper.SerializeResult(new
            {
                success = true,
                value = result
            });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Return the first-row, first-column value converted to a double.")]
    public string GetDouble(
        [Description("SQL query to execute")] string sql,
        [Description("Optional JSON parameters for parameterized queries")] string? parameters = null)
    {
        try
        {
            EnsureSafeSql(sql);
            using var db = _databaseConfig.CreateClient();
            var parsedParams = _databaseHelper.ParseParameters(parameters);

            var result = parsedParams != null
                ? db.Ado.GetDouble(sql, parsedParams)
                : db.Ado.GetDouble(sql);

            return _databaseHelper.SerializeResult(new
            {
                success = true,
                value = result
            });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Return the first-row, first-column value converted to a decimal.")]
    public string GetDecimal(
        [Description("SQL query to execute")] string sql,
        [Description("Optional JSON parameters for parameterized queries")] string? parameters = null)
    {
        try
        {
            EnsureSafeSql(sql);
            using var db = _databaseConfig.CreateClient();
            var parsedParams = _databaseHelper.ParseParameters(parameters);

            var result = parsedParams != null
                ? db.Ado.GetDecimal(sql, parsedParams)
                : db.Ado.GetDecimal(sql);

            return _databaseHelper.SerializeResult(new
            {
                success = true,
                value = result
            });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Return the first-row, first-column value converted to a DateTime, useful for timestamps.")]
    public string GetDateTime(
        [Description("SQL query to execute")] string sql,
        [Description("Optional JSON parameters for parameterized queries")] string? parameters = null)
    {
        try
        {
            EnsureSafeSql(sql);
            using var db = _databaseConfig.CreateClient();
            var parsedParams = _databaseHelper.ParseParameters(parameters);

            var result = parsedParams != null
                ? db.Ado.GetDateTime(sql, parsedParams)
                : db.Ado.GetDateTime(sql);

            return _databaseHelper.SerializeResult(new
            {
                success = true,
                value = result
            });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Execute SQL that must return two result sets (separated by a semicolon) and return firstResultSet/secondResultSet with row counts and data.")]
    public string SqlQueryMultiple(
        [Description("SQL statement containing two queries (separated by semicolon)")] string sql,
        [Description("Optional JSON parameters for parameterized queries")] string? parameters = null)
    {
        try
        {
            EnsureSafeSql(sql);
            using var db = _databaseConfig.CreateClient();
            var parsedParams = _databaseHelper.ParseParameters(parameters);

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

            var firstResultSet = _databaseHelper.ConvertDataTableToList(dataSet.Tables[0]);
            var secondResultSet = _databaseHelper.ConvertDataTableToList(dataSet.Tables[1]);

            return _databaseHelper.SerializeResult(new
            {
                success = true,
                firstResultSet = new { rowCount = firstResultSet.Count, data = firstResultSet },
                secondResultSet = new { rowCount = secondResultSet.Count, data = secondResultSet }
            });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Bind a JSON array to an IN parameter (inParameterName) plus optional otherParameters to safely execute IN-clause queries and return the rows.")]
    public string SqlQueryWithInParameter(
        [Description("SQL query containing IN parameter (e.g.: select * from [order] where id in (@ids))")] string sql,
        [Description("IN parameter name (e.g. \"ids\")")] string inParameterName,
        [Description("JSON array of IN parameter values (e.g.: [1,2,3])")] string inValues,
        [Description("JSON of other parameters (optional)")] string? otherParameters = null)
    {
        try
        {
            EnsureSafeSql(sql);
            using var db = _databaseConfig.CreateClient();

            var inArray = JsonSerializer.Deserialize<object[]>(inValues);
            if (inArray == null)
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "无效的 IN 参数值数组");

            var sugarParams = new List<SugarParameter>
            {
                new SugarParameter($"@{inParameterName.TrimStart('@')}", inArray)
            };

            if (!string.IsNullOrWhiteSpace(otherParameters))
            {
                var otherParams = _databaseHelper.ParseParameters(otherParameters);
                if (otherParams != null)
                {
                    sugarParams.AddRange(otherParams);
                }
            }

            var result = db.Ado.SqlQuery<dynamic>(sql, sugarParams.ToArray());

            return _databaseHelper.SerializeResult(new
            {
                success = true,
                rowCount = result.Count,
                data = result
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
            throw new DatabaseMcpException(DatabaseErrorCode.DangerousOperation, "检测到潜在危险操作，请使用 Schema 工具执行结构变更。");
        }
    }
}