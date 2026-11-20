using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using Microsoft.Extensions.Logging;
using SqlSugar;
using System.Data;

namespace DatabaseMcpServer.Operations;

/// <summary>
/// 数据库查询工具类，支持各种查询操作。
/// </summary>
internal class SqlQueryOperations : SqlOperationExecutor
{
    private const int MaxBatchQueryCount = 5;

    public Task<object> QueryTablePage(
        string tableName,
        int page = 1,
        int pageSize = 100,
        string? databaseName = null,
        int? maxResponseBytes = null,
        int? commandTimeoutSeconds = null,
        CancellationToken cancellationToken = default)
        => WithClientContextAsync(async context =>
        {
            if (page < 1 || (long)page * pageSize > int.MaxValue)
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "page 超出有效范围。");
            var limits = ExecutionOptions.ResolveQuery(pageSize, maxResponseBytes, commandTimeoutSeconds, bounded: true);
            SqlSafetyGuard.EnsureSafeTableName(tableName);
            var db = context.Client;
            var provider = db.CurrentConnectionConfig.DbType;
            if (!DatabaseProviderCatalog.SupportsBoundedResults(provider))
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

    public SqlQueryOperations(
        IDatabaseConfigService databaseConfig,
        IDatabaseHelperService databaseHelper,
        ILogger<SqlQueryOperations> logger, DatabaseExecutionOptions executionOptions, DatabaseMetadataCache metadataCache, SqlDatabaseSessions sessions)
        : base(databaseConfig, databaseHelper, logger, executionOptions, metadataCache, sessions)
    {
    }

    public Task<object> SqlQuerySingle(
        string sql,
        DatabaseParameter[]? parameters = null,
        int? commandTimeoutSeconds = null,
        CancellationToken cancellationToken = default,
        string? databaseName = null,
        int? maxResponseBytes = null)
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

    public Task<object> GetDataSetAll(
        string sql,
        DatabaseParameter[]? parameters = null,
        int? commandTimeoutSeconds = null,
        int? maxRows = null,
        int? maxResponseBytes = null,
        CancellationToken cancellationToken = default,
        string? databaseName = null)
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

    public Task<object> GetScalar(
        string sql,
        DatabaseParameter[]? parameters = null,
        int? commandTimeoutSeconds = null,
        CancellationToken cancellationToken = default,
        string? databaseName = null,
        int? maxResponseBytes = null)
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

    public Task<object> BatchSqlQuery(
        string[] queries,
        int? commandTimeoutSeconds = null,
        int? maxRows = null,
        int? maxResponseBytes = null,
        CancellationToken cancellationToken = default,
        string? databaseName = null)
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
