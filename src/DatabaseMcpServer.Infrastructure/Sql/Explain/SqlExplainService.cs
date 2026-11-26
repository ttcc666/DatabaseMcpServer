using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Services;

internal sealed class SqlExplainService(IDatabaseConfigService config, IDatabaseHelperService helper,
    ILogger<SqlExplainService> logger, DatabaseExecutionOptions options, DatabaseMetadataCache cache, SqlDatabaseSessions sessions,
    SqlServerPlanMode planMode)
    : SqlOperationExecutor(config, helper, logger, options, cache, sessions)
{
    public Task<object> Explain(string sql, string? databaseName, DatabaseParameter[]? parameters,
        int? maxResponseBytes, int? commandTimeoutSeconds, CancellationToken cancellationToken)
        => WithDeadlineAsync(async (context, token) =>
        {
            SqlSafetyGuard.EnsureExplainableSql(sql, DatabaseHelper);
            var provider = context.Client.CurrentConnectionConfig.DbType;
            ProviderCapabilities.Require(DatabaseProviderCatalog.Parse(context.DatabaseType), DatabaseCapability.EstimatedPlan);
            var limits = ExecutionOptions.ResolveQuery(null, maxResponseBytes, commandTimeoutSeconds, bounded: true);
            var bound = SqlParameterBinder.Bind(parameters, provider);
            if (provider == DbType.SqlServer && bound.Any(p => p.Value is Array and not byte[]))
                throw new DatabaseMcpException(DatabaseErrorCode.UnsupportedCapability, "SQL Server 估算计划目前仅支持标量参数；请使用多个标量参数表达 IN 列表。");
            var db = context.Client;
            var ado = db.Ado;
            using var open = ado.OpenAlways();
            var connection = (System.Data.Common.DbConnection)ado.Connection;
            var showplan = provider == DbType.SqlServer;
            await using var prepared = showplan ? await ((AdoProvider)ado).GetCommandAsync(sql, bound) : null;
            if (prepared != null) SqlServerPlanBatch.BindTypes(prepared);
            try
            {
                // Each SET is its own batch on the same exclusively leased physical connection.
                if (showplan) await planMode.SetAsync(connection, true, db.Ado.CommandTimeOut, token);
                var query = provider switch
                {
                    DbType.PostgreSQL => "EXPLAIN (FORMAT JSON, ANALYZE FALSE) " + sql,
                    DbType.MySql => "EXPLAIN FORMAT=JSON " + sql,
                    DbType.Sqlite => "EXPLAIN QUERY PLAN " + sql,
                    _ => sql
                };
                var budget = new BoundedQueryExecutor(limits.Rows, limits.Bytes, 1024);
                DatabaseQueryResult<BoundedJsonResult> result;
                if (showplan)
                {
                    // Retain the native command until its plan reader has been fully consumed.
                    var command = prepared!;
                    if (!ReferenceEquals(command.Connection, connection))
                        throw new InvalidOperationException("执行计划必须使用同一物理连接。");
                    await using var reader = await command.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess, token);
                    result = await budget.ReadResultAsync(context, reader, token, allResults: true);
                }
                else result = await budget.QueryResultAsync(context, query, bound, token, allResults: true);
                if (result.Data.Count == 0)
                    throw new DatabaseMcpException(DatabaseErrorCode.QueryExecutionFailed, "数据库未返回估算计划。");
                if (result.LimitReached)
                    throw new DatabaseMcpException(DatabaseErrorCode.ResponseLimitExceeded, "执行计划超过预算；请缩小查询或增加预算。");
                return (object)BoundedQueryExecutor.EncodeResponse(new ExplainQueryResult<BoundedJsonResult>
                {
                    Success = true,
                    DatabaseName = context.DatabaseName,
                    Provider = provider.ToString(),
                    ParameterMode = showplan ? "types-only" : "bound-values",
                    PlanFormat = provider switch { DbType.SqlServer => "showplan-xml", DbType.PostgreSQL or DbType.MySql => "json", _ => "query-plan-rows" },
                    Plan = result.Data
                }, limits.Bytes!.Value);
            }
            finally
            {
                if (showplan)
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    try
                    {
                        await planMode.SetAsync(connection, false, 5, cleanup.Token);
                    }
                    catch
                    {
                        // Never return a session with uncertain SHOWPLAN state to the driver pool.
                        if (connection is Microsoft.Data.SqlClient.SqlConnection sqlConnection)
                            Microsoft.Data.SqlClient.SqlConnection.ClearPool(sqlConnection);
                        connection.Close();
                        throw;
                    }
                }
            }
        }, commandTimeoutSeconds, cancellationToken, databaseName);
}
