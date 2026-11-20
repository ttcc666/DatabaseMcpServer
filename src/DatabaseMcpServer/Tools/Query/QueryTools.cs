using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Tools;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using SqlSugar;
using System.ComponentModel;
using System.Data;

namespace DatabaseMcpServer.Tools.Query;

/// <summary>
/// 数据库查询工具类，支持各种查询操作。
/// </summary>
[McpServerToolType]
internal class QueryTools : McpToolBase
{
    private const int MaxBatchQueryCount = 5;
    protected override bool RequiresSql => true;

    [McpServerTool(ReadOnly = true)]
    [Description("Read one table page ordered by its primary key using SqlSugar paging; no implicit COUNT. Requires a primary key. Concurrent writes may move rows between pages. For filters or complex SQL use query_database.")]
    public Task<string> QueryTablePage(
        [Description("Table or schema.table name; inspect get_table_schema first.")] string tableName,
        [Description("One-based page index.")] int page = 1,
        [Description("Rows per page, subject to server cap; default 100.")] int pageSize = 100,
        [Description("Optional database connection name.")] string? databaseName = null,
        [Description("Maximum tool JSON bytes; default server budget. Whole page fails if too large.")] int? maxResponseBytes = null,
        [Description("Command timeout in seconds, subject to server policy.")] int? commandTimeoutSeconds = null,
        CancellationToken cancellationToken = default)
        => WithClientContextAsync(async context =>
        {
            if (page < 1 || (long)page * pageSize > int.MaxValue)
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "page 超出有效范围。");
            var limits = ExecutionOptions.ResolveQuery(pageSize, maxResponseBytes, commandTimeoutSeconds, bounded: true);
            SqlSafetyGuard.EnsureSafeTableName(tableName);
            var db = context.Client;
            var provider = db.CurrentConnectionConfig.DbType;
            if (!DatabaseCapabilities.SupportsBoundedResults(provider))
                throw new DatabaseMcpException(DatabaseErrorCode.UnsupportedCapability, "此 provider 的表分页尚未验证。");
            return await SqlCommandTimeout.WithTimeoutAsync(db, limits.TimeoutSeconds, cancellationToken, async client =>
            {
                var columns = client.DbMaintenance.GetColumnInfosByTableName(tableName, false);
                var primary = columns.Where(c => c.IsPrimarykey).Select(c => c.DbColumnName).ToArray();
                if (primary.Length == 0)
                    throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "分页需要主键保证唯一排序；请为无主键表使用明确排序的查询。");
                foreach (var key in primary) SqlSafetyGuard.EnsureSafeTableName(key);
                var order = string.Join(",", primary.Select(key => SqlSafetyGuard.QuoteTableName(key, provider) + " asc"));
                // Official SqlQueryable pagination: only simple SELECT, external OrderBy, no total-count overload.
                using var table = await client.SqlQueryable<object>($"SELECT * FROM {SqlSafetyGuard.QuoteTableName(tableName, provider)}")
                    .OrderBy(order).ToDataTablePageAsync(page, pageSize);
                var data = DatabaseHelper.ConvertDataTableToList(table);
                return (object)BoundedQueryExecutor.EncodeResponse(new
                {
                    success = true, databaseName = context.DatabaseName, configurationVersion = context.ConfigurationVersion,
                    page, pageSize, rowCount = data.Count, data, orderBy = primary,
                    mayHaveMore = data.Count == pageSize, consistency = "provider-isolation; no cross-page snapshot"
                }, limits.Bytes!.Value);
            });
        }, cancellationToken, databaseName);

    public QueryTools(
        IDatabaseConfigService databaseConfig,
        IDatabaseHelperService databaseHelper,
        IJsonResultSerializer resultSerializer,
        ILogger<QueryTools> logger, DatabaseExecutionOptions executionOptions, DatabaseMcpServer.Services.DatabaseMetadataCache metadataCache)
        : base(databaseConfig, databaseHelper, resultSerializer, logger, executionOptions, metadataCache)
    {
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Execute a read-only SQL statement and return only the first row (or null) with the same typed parameters.")]
    public Task<string> SqlQuerySingle(
        [Description("SQL query to execute")] string sql,
        [Description("Typed parameter array. NULL requires dbType; output parameters are not allowed.")] DatabaseParameter[]? parameters = null,
        [Description("Optional SQL command timeout in seconds. Omit to use the provider default (typically 300). 0 means wait indefinitely.")] int? commandTimeoutSeconds = null,
        CancellationToken cancellationToken = default,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null,
        [Description("Maximum tool JSON UTF-8 bytes; oversized values return an error without truncating the value.")] int? maxResponseBytes = null)
    {
        return WithClientAsync(async db =>
        {
            var limits = ExecutionOptions.ResolveQuery(null, maxResponseBytes, commandTimeoutSeconds);
            EnsureSafeSql(sql);
            var parsedParams = SqlParameterBinder.Bind(parameters, db.CurrentConnectionConfig.DbType);
            var result = await SqlCommandTimeout.WithTimeoutAsync(db, limits.TimeoutSeconds, cancellationToken, async client =>
            {
                using var reader = await client.Ado.GetDataReaderAsync(sql, parsedParams);
                return await QueryReader.ReadAsync(reader, cancellationToken) ? client.Utilities.DataReaderToExpandoObject(reader) : null;
            });

            var response = new { success = true, data = result };
            return limits.Bytes is { } bytes ? BoundedQueryExecutor.EncodeResponse(response, bytes) : (object)response;
        }, cancellationToken, databaseName);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Execute SQL that may contain multiple SELECT statements and return each result set with its rowCount inside resultSets. Values retain the driver reader types; duplicate column names receive unique suffixes.")]
    public Task<string> GetDataSetAll(
        [Description("SQL query to execute (can contain multiple query statements separated by semicolons)")] string sql,
        [Description("Typed parameter array. NULL requires dbType; output parameters are not allowed.")] DatabaseParameter[]? parameters = null,
        [Description("Optional SQL command timeout in seconds. Omit to use the provider default (typically 300). 0 means wait indefinitely.")] int? commandTimeoutSeconds = null,
        [Description("Optional positive total returned rows across all result sets. Supports SQLite, MySQL, PostgreSQL and SQL Server.")] int? maxRows = null,
        [Description("Optional maximum tool JSON UTF-8 bytes (minimum 256), shared across result sets.")] int? maxResponseBytes = null,
        CancellationToken cancellationToken = default,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
    {
        return WithClientAsync(async db =>
        {
            EnsureSafeSql(sql, allowMultipleStatements: true);
            var limits = ExecutionOptions.ResolveQuery(maxRows, maxResponseBytes, commandTimeoutSeconds);
            maxRows = limits.Rows; maxResponseBytes = limits.Bytes; commandTimeoutSeconds = limits.TimeoutSeconds;
            var parsedParams = SqlParameterBinder.Bind(parameters, db.CurrentConnectionConfig.DbType);
            var budget = new BoundedQueryExecutor(maxRows, maxResponseBytes);
            return await SqlCommandTimeout.WithTimeoutAsync(db, commandTimeoutSeconds, cancellationToken,
                client => budget.MultipleAsync(client, sql, parsedParams, cancellationToken));
        }, cancellationToken, databaseName);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Return the first-row, first-column value from the SQL statement—ideal for COUNT/SUM scalar queries.")]
    public Task<string> GetScalar(
        [Description("SQL query to execute")] string sql,
        [Description("Typed parameter array. NULL requires dbType; output parameters are not allowed.")] DatabaseParameter[]? parameters = null,
        [Description("Optional SQL command timeout in seconds. Omit to use the provider default (typically 300). 0 means wait indefinitely.")] int? commandTimeoutSeconds = null,
        CancellationToken cancellationToken = default,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null,
        [Description("Maximum tool JSON UTF-8 bytes; oversized values return an error without truncating the value.")] int? maxResponseBytes = null)
    {
        return WithClientAsync(async db =>
        {
            var limits = ExecutionOptions.ResolveQuery(null, maxResponseBytes, commandTimeoutSeconds);
            EnsureSafeSql(sql);
            var parsedParams = SqlParameterBinder.Bind(parameters, db.CurrentConnectionConfig.DbType);
            var result = await SqlCommandTimeout.WithTimeoutAsync(db, limits.TimeoutSeconds, cancellationToken, client =>
                client.Ado.GetScalarAsync(sql, parsedParams));

            var response = new { success = true, value = result };
            return limits.Bytes is { } bytes ? BoundedQueryExecutor.EncodeResponse(response, bytes) : (object)response;
        }, cancellationToken, databaseName);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Execute 1-5 independent read-only SQL queries sequentially over one connection and return success, rowCount, data, or error per query.")]
    public Task<string> BatchSqlQuery(
        [Description("Read-only SQL queries as a JSON string array (maximum: 5)")] string[] queries,
        [Description("Optional SQL command timeout in seconds applied to every query in the batch. Omit to use the provider default (typically 300). 0 means wait indefinitely.")] int? commandTimeoutSeconds = null,
        [Description("Optional positive total returned rows across the batch; remaining queries are marked not executed.")] int? maxRows = null,
        [Description("Optional maximum tool JSON UTF-8 bytes across the batch; minimum 256 + 768 per query for metadata and errors.")] int? maxResponseBytes = null,
        CancellationToken cancellationToken = default,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
    {
        return WithClientAsync(async db =>
        {
            var limits = ExecutionOptions.ResolveQuery(maxRows, maxResponseBytes, commandTimeoutSeconds);
            maxRows = limits.Rows; maxResponseBytes = limits.Bytes; commandTimeoutSeconds = limits.TimeoutSeconds;
            var queryList = ParseBatchQueries(queries);
            var budget = new BoundedQueryExecutor(maxRows, maxResponseBytes, 256 + 768 * queryList.Length);
            return await SqlCommandTimeout.WithTimeoutAsync(db, commandTimeoutSeconds, cancellationToken,
                client => budget.BatchAsync(client, queryList, query => EnsureSafeSql(query), cancellationToken));
        }, cancellationToken, databaseName);
    }

    private static string[] ParseBatchQueries(string[] queries)
    {
        if (queries == null || queries.Any(string.IsNullOrWhiteSpace))
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "queries 必须是非空 SQL 字符串数组。");
        var queryList = queries;

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
