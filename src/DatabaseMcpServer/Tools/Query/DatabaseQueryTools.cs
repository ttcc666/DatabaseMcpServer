using System.ComponentModel;
using System.Text;
using System.Text.Json;
using DatabaseMcpServer.Filters;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DatabaseMcpServer.Tools.Query;

[McpServerToolType]
internal sealed class DatabaseQueryTools(DatabaseQueryService queries, DatabaseExecutionOptions options, ILogger<DatabaseQueryTools> logger)
{
    [McpServerTool(ReadOnly = true, UseStructuredContent = true, OutputSchemaType = typeof(DatabaseQueryResult))]
    [Description("Execute a read-only database query. Explicit database target, typed JSON parameters, bounded rows/bytes and structured results. Returns the actual database and configuration version. Use schema tools before writing SQL; limitReached means use narrower filters or stable pagination.")]
    public async Task<CallToolResult> QueryDatabase(
        [Description("One read-only SQL statement; bind values using parameters.")] string sql,
        [Description("Connection name; omitted uses a snapshot of the current database.")] string? databaseName = null,
        [Description("Typed parameter array. NULL requires dbType. Bind IN (@ids) with an array value. Output parameters are not allowed.")] DatabaseParameter[]? parameters = null,
        [Description("Returned row limit; default 1000, subject to server policy.")] int? maxRows = null,
        [Description("Tool JSON UTF-8 budget; default 1 MiB, minimum 1024. Protocol text/structured envelopes add overhead.")] int? maxResponseBytes = null,
        [Description("Command timeout; default 30 seconds, subject to server policy.")] int? commandTimeoutSeconds = null,
        CancellationToken cancellationToken = default)
    {
        using var measurement = DatabaseTelemetry.MeasureRequest();
        DatabaseQueryResult result;
        try
        {
            result = await queries.QueryAsync(sql, databaseName, parameters, maxRows, maxResponseBytes, commandTimeoutSeconds, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { DatabaseTelemetry.Cancelled.Add(1); throw; }
        catch (Exception ex)
        {
            using var error = JsonDocument.Parse(McpExceptionFilter.HandleException(ex, logger));
            result = new() { Success = false, ErrorCode = error.RootElement.GetProperty("errorCode").GetInt32(),
                ErrorMessage = error.RootElement.GetProperty("errorMessage").GetString() };
        }

        var json = DatabaseTelemetry.Serialize(() => JsonSerializer.Serialize(result, JsonSerializationDefaults.CompactCamelCase));
        var maximum = maxResponseBytes ?? Math.Min(options.DefaultResponseBytes, options.MaxResponseBytes);
        if (result.Success && Encoding.UTF8.GetByteCount(json) > maximum)
        {
            result = new() { Success = false, ErrorCode = (int)DatabaseErrorCode.ResponseLimitExceeded,
                ErrorMessage = "结构化响应元数据超出字节预算，请增加预算或缩小查询。" };
            json = JsonSerializer.Serialize(result, JsonSerializationDefaults.CompactCamelCase);
        }
        using var document = JsonDocument.Parse(json);
        return new() { IsError = !result.Success, Content = [new TextContentBlock { Text = json }], StructuredContent = document.RootElement.Clone() };
    }
}
