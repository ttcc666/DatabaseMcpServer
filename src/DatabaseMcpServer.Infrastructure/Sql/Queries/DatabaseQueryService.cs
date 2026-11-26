using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DatabaseMcpServer.Services;

internal sealed class DatabaseQueryService(SqlRequestExecutor requests, IDatabaseHelperService helper,
    DatabaseExecutionOptions options, ILogger<DatabaseQueryService> logger)
{
    public async Task<(BoundedJsonResult Payload, bool Success)> ExecuteAsync(string sql, string? databaseName,
        IReadOnlyList<DatabaseParameter>? parameters, int? maxRows, int? maxResponseBytes,
        int? timeout, CancellationToken cancellationToken)
    {
        var result = await requests.ExecuteAsync<DatabaseQueryResult<BoundedJsonResult>>(async (context, token) =>
        {
            SqlSafetyGuard.EnsureReadOnlySql(sql, helper);
            var limits = options.ResolveQuery(maxRows, maxResponseBytes, timeout, bounded: true);
            if (limits.Bytes < 1024)
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "结构化查询至少需要 1024 字节预算以容纳目标与错误元数据。");
            var bound = SqlParameterBinder.Bind(parameters, context.Client.CurrentConnectionConfig.DbType);
            return await new BoundedQueryExecutor(limits.Rows, limits.Bytes, 1024).QueryResultAsync(context, sql, bound, token);
        }, DatabaseToolAccess.Read, timeout, cancellationToken, databaseName, logger,
            error => new() { Success = false, ErrorCode = (int?)error.ErrorCode, ErrorMessage = error.ErrorMessage,
                Retryable = error.Retryable, Phase = error.Phase, Capability = error.Capability, Hint = error.Hint });

        var json = DatabaseTelemetry.Serialize(() => JsonSerializer.SerializeToUtf8Bytes(result, JsonSerializationDefaults.CompactCamelCase));
        var maximum = maxResponseBytes ?? Math.Min(options.DefaultResponseBytes, options.MaxResponseBytes);
        if (result.Success && json.Length > maximum)
        {
            result = new()
            {
                Success = false,
                ErrorCode = (int)DatabaseErrorCode.ResponseLimitExceeded,
                ErrorMessage = "结构化响应元数据超出字节预算，请增加预算或缩小查询。", Retryable = false, Phase = "serialization"
            };
            json = JsonSerializer.SerializeToUtf8Bytes(result, JsonSerializationDefaults.CompactCamelCase);
        }
        return (new(json), result.Success);
    }
}
