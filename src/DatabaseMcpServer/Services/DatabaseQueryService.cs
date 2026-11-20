using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;

namespace DatabaseMcpServer.Services;

internal sealed class DatabaseQueryService(IDatabaseConfigService databases, IDatabaseHelperService helper, DatabaseExecutionOptions options)
{
    public async Task<DatabaseQueryResult> QueryAsync(string sql, string? databaseName,
        IReadOnlyList<DatabaseParameter>? parameters, int? maxRows, int? maxResponseBytes,
        int? timeout, CancellationToken cancellationToken)
    {
        SqlSafetyGuard.EnsureReadOnlySql(sql, helper);
        var limits = options.ResolveQuery(maxRows, maxResponseBytes, timeout, bounded: true);
        if (limits.Bytes < 1024)
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "结构化查询至少需要 1024 字节预算以容纳目标与错误元数据。");
        using var lease = await (databaseName == null ? databases.AcquireClientAsync(cancellationToken)
            : databases.AcquireClientAsync(databaseName, cancellationToken));
        DatabaseAccessPolicy.EnsureAllowed(lease.Context, DatabaseToolAccess.Read, options.Access);
        var context = lease.Context;
        DatabaseProviderCatalog.EnsureSqlTools(context.Client.CurrentConnectionConfig.DbType);
        var bound = SqlParameterBinder.Bind(parameters, context.Client.CurrentConnectionConfig.DbType);
        try
        {
            return await SqlCommandTimeout.WithTimeoutAsync(context.Client, limits.TimeoutSeconds, cancellationToken,
                _ => new BoundedQueryExecutor(limits.Rows, limits.Bytes, 1024)
                    .QueryResultAsync(context, sql, bound, cancellationToken));
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
    }
}
