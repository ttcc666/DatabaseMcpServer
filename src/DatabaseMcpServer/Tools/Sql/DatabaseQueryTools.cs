using System.ComponentModel;
using System.Text;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DatabaseMcpServer.Tools.Query;

[McpServerToolType]
internal sealed class DatabaseQueryTools(DatabaseQueryService queries)
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
        var response = await queries.ExecuteAsync(sql, databaseName, parameters, maxRows, maxResponseBytes,
            commandTimeoutSeconds, cancellationToken);
        return McpResultAdapter.StructuredJson(Encoding.UTF8.GetString(response.Payload.Utf8Json), response.Success);
    }
}
