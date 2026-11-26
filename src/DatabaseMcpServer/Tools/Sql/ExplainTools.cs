using System.ComponentModel;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DatabaseMcpServer.Tools.Query;

[ToolGroup("query", "sql", DatabaseToolAccess.Read)]
[McpServerToolType]
internal sealed class ExplainTools(SqlExplainService service)
{
    [ToolPolicy(Analysis = true, Capability = DatabaseMcpServer.Helpers.DatabaseCapability.EstimatedPlan)]
    [McpServerTool(Title = "Explain Query", ReadOnly = true, UseStructuredContent = true, OutputSchemaType = typeof(ExplainQueryResult))]
    [Description("Get an estimated execution plan for one SELECT/CTE without executing the target query. SQL Server requires SHOWPLAN; PostgreSQL uses ANALYZE FALSE; MySQL uses EXPLAIN FORMAT=JSON; SQLite uses EXPLAIN QUERY PLAN. Planning is not a security sandbox for functions: use a database read-only role. No actual execution statistics or universal cost units.")]
    public Task<CallToolResult> ExplainQuery(
        [Description("One read-only SELECT or WITH query; no EXPLAIN/ANALYZE, SET or multiple statements.")] string sql,
        [Description("Optional connection name.")] string? databaseName = null,
        [Description("Typed input parameters, including dbType for NULL. SQL Server supports scalar types only and plans typed locals without applying values (parameterMode=types-only).")] DatabaseParameter[]? parameters = null,
        [Description("Maximum tool JSON UTF-8 bytes; oversized plans fail.")] int? maxResponseBytes = null,
        [Description("Request timeout in seconds, subject to server cap.")] int? commandTimeoutSeconds = null,
        CancellationToken cancellationToken = default)
        => McpResultAdapter.Structured(service.Explain(sql, databaseName, parameters, maxResponseBytes, commandTimeoutSeconds, cancellationToken));
}
