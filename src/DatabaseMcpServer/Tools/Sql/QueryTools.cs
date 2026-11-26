using System.ComponentModel;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Operations;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;

namespace DatabaseMcpServer.Tools.Query;

[ToolGroup("query", "sql", DatabaseMcpServer.Helpers.DatabaseToolAccess.Read)]
[McpServerToolType]
internal sealed class QueryTools(SqlQueryOperations operations)
{
    [ToolPolicy(Analysis = true, Capability = DatabaseMcpServer.Helpers.DatabaseCapability.BoundedResults)]
    [McpServerTool(Title = "Query Table Page", ReadOnly = true, UseStructuredContent = true, OutputSchemaType = typeof(TablePageResult))]
    [Description("Query a table page with non-null primary keys. Without columns/filters/orderBy, stream rows with byte checks and use nextCursor for keyset continuation. Supplying any of these options uses SqlSugar page-based offset pagination, loads the whole page before checking response bytes, appends missing primary keys to sorting, and returns no cursor. No COUNT or cross-page snapshot; NULL ordering follows the database.")]
    public Task<CallToolResult> QueryTablePage(
        [Description("Table or schema.table name; inspect get_table_schema first.")] string tableName,
        [Description("One-based page index.")] int page = 1,
        [Description("Rows per page, subject to server cap; default 100.")] int pageSize = 100,
        [Description("Optional database connection name.")] string? databaseName = null,
        [Description("Maximum tool JSON bytes; default server budget. Whole page fails if too large. With columns/filters/orderBy, checked after loading the page; does not bound read memory.")] int? maxResponseBytes = null,
        [Description("Total deadline in seconds including admission, execution and reading; subject to server policy.")] int? commandTimeoutSeconds = null,
        CancellationToken cancellationToken = default,
        [Description("Opaque nextCursor with identical configuration content/path, working directory, database, table and pageSize; use page=1 and omit columns/filters/orderBy.")] string? cursor = null,
        [Description("Optional 1-64 unique column names, case-sensitive; omit for all columns. No expressions or aliases.")] string[]? columns = null,
        [Description("Optional up to 32 AND conditions {column, op, value?, dbType?}; only scalar comparisons and isNull/isNotNull.")] TableFilter[]? filters = null,
        [Description("Optional up to 8 unique {column, direction} entries; direction defaults to asc. Missing primary keys are appended ascending.")] TableOrder[]? orderBy = null)
        => McpResultAdapter.Structured(operations.QueryTablePage(tableName, page, pageSize, databaseName, maxResponseBytes, commandTimeoutSeconds, cancellationToken, cursor, columns, filters, orderBy));

    [ToolPolicy(Capability = DatabaseMcpServer.Helpers.DatabaseCapability.BoundedResults)]
    [McpServerTool(Title = "Get Multiple Result Sets", ReadOnly = true)]
    [Description("Execute SQL that may contain multiple SELECT statements and return each result set with its rowCount inside resultSets. Values retain the driver reader types; duplicate column names receive unique suffixes.")]
    public Task<string> GetDataSetAll(
        [Description("SQL query to execute (can contain multiple query statements separated by semicolons)")] string sql,
        [Description("Typed parameter array. NULL requires dbType; output parameters are not allowed.")] DatabaseParameter[]? parameters = null,
        [Description("Total deadline including queue, execution and all result sets; default server query timeout (normally 30 seconds). 0 requires server permission for infinite timeout.")] int? commandTimeoutSeconds = null,
        [Description("Total rows across result sets; defaults to server budget (normally 1000), unless default budgets are disabled. Supports SQLite, MySQL, PostgreSQL and SQL Server.")] int? maxRows = null,
        [Description("Maximum tool JSON UTF-8 bytes (minimum 256), shared across result sets; defaults to server budget (normally 1 MiB), unless default budgets are disabled.")] int? maxResponseBytes = null,
        CancellationToken cancellationToken = default,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.GetDataSetAll(sql, parameters, commandTimeoutSeconds, maxRows, maxResponseBytes, cancellationToken, databaseName));

    [McpServerTool(Title = "Batch SQL Query", ReadOnly = true)]
    [Description("Execute 1-5 independent read-only SQL queries sequentially over one connection and return success, rowCount, data, or error per query.")]
    public Task<string> BatchSqlQuery(
        [Description("Typed read-only commands [{sql, parameters?}] (1-5). Each command binds its own parameters.")] DatabaseCommand[] commands,
        [Description("Total deadline including queue and all queries; default server query timeout (normally 30 seconds). 0 requires server permission for infinite timeout.")] int? commandTimeoutSeconds = null,
        [Description("Total rows across the batch; defaults to server budget (normally 1000), unless default budgets are disabled. Remaining queries are marked not executed.")] int? maxRows = null,
        [Description("Maximum tool JSON UTF-8 bytes across the batch; defaults to server budget (normally 1 MiB), unless default budgets are disabled. Minimum 256 + 768 per query for metadata and errors.")] int? maxResponseBytes = null,
        CancellationToken cancellationToken = default,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.BatchSqlQuery(commands, commandTimeoutSeconds, maxRows, maxResponseBytes, cancellationToken, databaseName));
}
