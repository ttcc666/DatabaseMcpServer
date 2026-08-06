using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Tools;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using SqlSugar;
using System.ComponentModel;
using System.Data;
using System.Text.Json;

namespace DatabaseMcpServer.Tools.Query;

/// <summary>
/// 数据库查询工具类，支持各种查询操作。
/// </summary>
[McpServerToolType]
internal class QueryTools : McpToolBase
{
    private const int MaxBatchQueryCount = 5;

    public QueryTools(
        IDatabaseConfigService databaseConfig,
        IDatabaseHelperService databaseHelper,
        IJsonResultSerializer resultSerializer,
        ILogger<QueryTools> logger)
        : base(databaseConfig, databaseHelper, resultSerializer, logger)
    {
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Execute a read-only SQL statement with dangerous-operation detection, optionally binding JSON parameters, and return rowCount plus data.")]
    public string SqlQuery(
        [Description("SQL query to execute")] string sql,
        [Description("Optional JSON parameters for parameterized queries")] string? parameters = null,
        [Description("Optional SQL command timeout in seconds. Omit to use the provider default (typically 300). 0 means wait indefinitely.")] int? commandTimeoutSeconds = null)
    {
        return WithClient(db =>
        {
            EnsureSafeSql(sql);
            var parsedParams = DatabaseHelper.ParseParameters(parameters);
            var result = SqlCommandTimeout.WithTimeout(db, commandTimeoutSeconds, client =>
                parsedParams != null
                    ? client.Ado.SqlQuery<dynamic>(sql, parsedParams)
                    : client.Ado.SqlQuery<dynamic>(sql));

            return new
            {
                success = true,
                rowCount = result.Count,
                data = result
            };
        });
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Execute a read-only SQL statement and return only the first row (or null) with the same optional JSON parameters.")]
    public string SqlQuerySingle(
        [Description("SQL query to execute")] string sql,
        [Description("Optional JSON parameters for parameterized queries")] string? parameters = null,
        [Description("Optional SQL command timeout in seconds. Omit to use the provider default (typically 300). 0 means wait indefinitely.")] int? commandTimeoutSeconds = null)
    {
        return WithClient(db =>
        {
            EnsureSafeSql(sql);
            var parsedParams = DatabaseHelper.ParseParameters(parameters);
            var result = SqlCommandTimeout.WithTimeout(db, commandTimeoutSeconds, client =>
            {
                var rows = parsedParams != null
                    ? client.Ado.SqlQuery<dynamic>(sql, parsedParams)
                    : client.Ado.SqlQuery<dynamic>(sql);
                return rows.FirstOrDefault();
            });

            return new
            {
                success = true,
                data = result
            };
        });
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Execute SQL that may contain multiple SELECT statements and return each result set with its rowCount inside resultSets.")]
    public string GetDataSetAll(
        [Description("SQL query to execute (can contain multiple query statements separated by semicolons)")] string sql,
        [Description("Optional JSON parameters for parameterized queries")] string? parameters = null,
        [Description("Optional SQL command timeout in seconds. Omit to use the provider default (typically 300). 0 means wait indefinitely.")] int? commandTimeoutSeconds = null)
    {
        return WithClient(db =>
        {
            EnsureSafeSql(sql, allowMultipleStatements: true);
            var parsedParams = DatabaseHelper.ParseParameters(parameters);
            var dataSet = SqlCommandTimeout.WithTimeout(db, commandTimeoutSeconds, client =>
                parsedParams != null
                    ? client.Ado.GetDataSetAll(sql, parsedParams)
                    : client.Ado.GetDataSetAll(sql));

            var resultSets = new List<object>();
            foreach (DataTable table in dataSet.Tables)
            {
                var rows = DatabaseHelper.ConvertDataTableToList(table);
                resultSets.Add(new { rowCount = rows.Count, data = rows });
            }

            return new
            {
                success = true,
                resultSetCount = resultSets.Count,
                resultSets
            };
        });
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Return the first-row, first-column value from the SQL statement—ideal for COUNT/SUM scalar queries.")]
    public string GetScalar(
        [Description("SQL query to execute")] string sql,
        [Description("Optional JSON parameters for parameterized queries")] string? parameters = null,
        [Description("Optional SQL command timeout in seconds. Omit to use the provider default (typically 300). 0 means wait indefinitely.")] int? commandTimeoutSeconds = null)
    {
        return WithClient(db =>
        {
            EnsureSafeSql(sql);
            var parsedParams = DatabaseHelper.ParseParameters(parameters);
            var result = SqlCommandTimeout.WithTimeout(db, commandTimeoutSeconds, client =>
                parsedParams != null
                    ? client.Ado.GetScalar(sql, parsedParams)
                    : client.Ado.GetScalar(sql));

            return new
            {
                success = true,
                value = result
            };
        });
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Bind a JSON array to an IN parameter (inParameterName) plus optional otherParameters to safely execute IN-clause queries and return the rows.")]
    public string SqlQueryWithInParameter(
        [Description("SQL query containing IN parameter (e.g.: select * from [order] where id in (@ids))")] string sql,
        [Description("IN parameter name (e.g. \"ids\")")] string inParameterName,
        [Description("JSON array of IN parameter values (e.g.: [1,2,3])")] string inValues,
        [Description("JSON of other parameters (optional)")] string? otherParameters = null,
        [Description("Optional SQL command timeout in seconds. Omit to use the provider default (typically 300). 0 means wait indefinitely.")] int? commandTimeoutSeconds = null)
    {
        return WithClient(db =>
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
                var otherParams = DatabaseHelper.ParseParameters(otherParameters);
                if (otherParams != null)
                {
                    sugarParams.AddRange(otherParams);
                }
            }

            var result = SqlCommandTimeout.WithTimeout(db, commandTimeoutSeconds, client =>
                client.Ado.SqlQuery<dynamic>(sql, sugarParams.ToArray()));
            return new
            {
                success = true,
                rowCount = result.Count,
                data = result
            };
        });
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Execute 1-5 independent read-only SQL queries sequentially over one connection and return success, rowCount, data, or error per query.")]
    public string BatchSqlQuery(
        [Description("Read-only SQL queries as a JSON string array (maximum: 5)")] JsonElement queries,
        [Description("Optional SQL command timeout in seconds applied to every query in the batch. Omit to use the provider default (typically 300). 0 means wait indefinitely.")] int? commandTimeoutSeconds = null)
    {
        return WithClient(db =>
        {
            var queryList = ParseBatchQueries(queries);

            return SqlCommandTimeout.WithTimeout(db, commandTimeoutSeconds, client =>
            {
                var results = new List<object>(queryList.Length);
                var successfulQueries = 0;

                using (client.Ado.OpenAlways())
                {
                    for (var i = 0; i < queryList.Length; i++)
                    {
                        try
                        {
                            var sql = queryList[i];
                            EnsureSafeSql(sql);
                            var rows = client.Ado.SqlQuery<dynamic>(sql);
                            results.Add(new { success = true, queryIndex = i, rowCount = rows.Count, data = rows });
                            successfulQueries++;
                        }
                        catch (Exception ex)
                        {
                            results.Add(new { success = false, queryIndex = i, error = ex.Message });
                        }
                    }
                }

                return new
                {
                    success = true,
                    totalQueries = queryList.Length,
                    successfulQueries,
                    failedQueries = queryList.Length - successfulQueries,
                    results
                };
            });
        });
    }

    private static string[] ParseBatchQueries(JsonElement queries)
    {
        if (queries.ValueKind != JsonValueKind.Array)
        {
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "queries 必须是 JSON 字符串数组");
        }

        var queryList = queries
            .EnumerateArray()
            .Select(item =>
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "queries 数组中的每一项必须是字符串 SQL");
                }

                var sql = item.GetString();
                if (string.IsNullOrWhiteSpace(sql))
                {
                    throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "queries 数组中包含空 SQL");
                }

                return sql;
            })
            .ToArray();

        if (queryList.Length == 0)
        {
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "queries 数组不能为空");
        }

        if (queryList.Length > MaxBatchQueryCount)
        {
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, $"一次最多允许执行 {MaxBatchQueryCount} 条查询");
        }

        return queryList;
    }

    private void EnsureSafeSql(string sql, bool allowMultipleStatements = false)
    {
        SqlSafetyGuard.EnsureReadOnlySql(sql, DatabaseHelper, allowMultipleStatements);
    }
}
