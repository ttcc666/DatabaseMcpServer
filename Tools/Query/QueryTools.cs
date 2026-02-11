using DatabaseMcpServer.Filters;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using SqlSugar;
using System.ComponentModel;
using System.Data;
using System.Text.Json;

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
    [Description("Execute SQL that may contain multiple SELECT statements and return each result set with its rowCount inside resultSets.")]
    public string GetDataSetAll(
        [Description("SQL query to execute (can contain multiple query statements separated by semicolons)")] string sql,
        [Description("Optional JSON parameters for parameterized queries")] string? parameters = null)
    {
        try
        {
            EnsureSafeSql(sql, allowMultipleStatements: true);
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
                resultSets
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

            if (string.IsNullOrWhiteSpace(inParameterName))
            {
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "inParameterName 不能为空");
            }

            if (string.IsNullOrWhiteSpace(inValues))
            {
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "inValues 不能为空");
            }

            using var db = _databaseConfig.CreateClient();

            var inArray = JsonSerializer.Deserialize<object[]>(inValues);
            if (inArray == null)
            {
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "无效的 IN 参数值数组");
            }

            var sugarParams = new List<SugarParameter>
            {
                new($"@{inParameterName.TrimStart('@')}", inArray)
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

    private void EnsureSafeSql(string sql, bool allowMultipleStatements = false)
    {
        SqlSafetyGuard.EnsureReadOnlySql(sql, _databaseHelper, allowMultipleStatements);
    }
}
