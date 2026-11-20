using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DatabaseMcpServer.Services;

internal sealed class DatabaseQueryService(SqlDatabaseSessions databases, IDatabaseHelperService helper,
    DatabaseExecutionOptions options, ILogger<DatabaseQueryService> logger)
{
    public async Task<(BoundedJsonResult Payload, bool Success)> ExecuteAsync(string sql, string? databaseName,
        IReadOnlyList<DatabaseParameter>? parameters, int? maxRows, int? maxResponseBytes,
        int? timeout, CancellationToken cancellationToken)
    {
        using var measurement = DatabaseTelemetry.MeasureRequest();
        DatabaseQueryResult result;
        try
        {
            result = await QueryAsync(sql, databaseName, parameters, maxRows, maxResponseBytes, timeout, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DatabaseTelemetry.Cancelled.Add(1);
            throw;
        }
        catch (Exception ex)
        {
            var error = DatabaseErrorMapper.Map(ex, logger);
            result = new() { Success = false, ErrorCode = (int?)error.ErrorCode, ErrorMessage = error.ErrorMessage };
        }

        var json = DatabaseTelemetry.Serialize(() => JsonSerializer.SerializeToUtf8Bytes(result, JsonSerializationDefaults.CompactCamelCase));
        var maximum = maxResponseBytes ?? Math.Min(options.DefaultResponseBytes, options.MaxResponseBytes);
        if (result.Success && json.Length > maximum)
        {
            result = new()
            {
                Success = false,
                ErrorCode = (int)DatabaseErrorCode.ResponseLimitExceeded,
                ErrorMessage = "结构化响应元数据超出字节预算，请增加预算或缩小查询。"
            };
            json = JsonSerializer.SerializeToUtf8Bytes(result, JsonSerializationDefaults.CompactCamelCase);
        }
        return (new(json), result.Success);
    }

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
