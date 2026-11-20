using System.ComponentModel;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Operations;
using ModelContextProtocol.Server;

namespace DatabaseMcpServer.Tools.Query;

[McpServerToolType]
internal sealed class QueryTools(SqlQueryOperations operations)
{
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
        => McpResultAdapter.Text(operations.QueryTablePage(tableName, page, pageSize, databaseName, maxResponseBytes, commandTimeoutSeconds, cancellationToken));

    [McpServerTool(ReadOnly = true)]
    [Description("Execute a read-only SQL statement and return only the first row (or null) with the same typed parameters.")]
    public Task<string> SqlQuerySingle(
        [Description("SQL query to execute")] string sql,
        [Description("Typed parameter array. NULL requires dbType; output parameters are not allowed.")] DatabaseParameter[]? parameters = null,
        [Description("Optional SQL command timeout in seconds. Omit to use the provider default (typically 300). 0 means wait indefinitely.")] int? commandTimeoutSeconds = null,
        CancellationToken cancellationToken = default,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null,
        [Description("Maximum tool JSON UTF-8 bytes; oversized values return an error without truncating the value.")] int? maxResponseBytes = null)
        => McpResultAdapter.Text(operations.SqlQuerySingle(sql, parameters, commandTimeoutSeconds, cancellationToken, databaseName, maxResponseBytes));

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
        => McpResultAdapter.Text(operations.GetDataSetAll(sql, parameters, commandTimeoutSeconds, maxRows, maxResponseBytes, cancellationToken, databaseName));

    [McpServerTool(ReadOnly = true)]
    [Description("Return the first-row, first-column value from the SQL statement—ideal for COUNT/SUM scalar queries.")]
    public Task<string> GetScalar(
        [Description("SQL query to execute")] string sql,
        [Description("Typed parameter array. NULL requires dbType; output parameters are not allowed.")] DatabaseParameter[]? parameters = null,
        [Description("Optional SQL command timeout in seconds. Omit to use the provider default (typically 300). 0 means wait indefinitely.")] int? commandTimeoutSeconds = null,
        CancellationToken cancellationToken = default,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null,
        [Description("Maximum tool JSON UTF-8 bytes; oversized values return an error without truncating the value.")] int? maxResponseBytes = null)
        => McpResultAdapter.Text(operations.GetScalar(sql, parameters, commandTimeoutSeconds, cancellationToken, databaseName, maxResponseBytes));

    [McpServerTool(ReadOnly = true)]
    [Description("Execute 1-5 independent read-only SQL queries sequentially over one connection and return success, rowCount, data, or error per query.")]
    public Task<string> BatchSqlQuery(
        [Description("Read-only SQL queries as a JSON string array (maximum: 5)")] string[] queries,
        [Description("Optional SQL command timeout in seconds applied to every query in the batch. Omit to use the provider default (typically 300). 0 means wait indefinitely.")] int? commandTimeoutSeconds = null,
        [Description("Optional positive total returned rows across the batch; remaining queries are marked not executed.")] int? maxRows = null,
        [Description("Optional maximum tool JSON UTF-8 bytes across the batch; minimum 256 + 768 per query for metadata and errors.")] int? maxResponseBytes = null,
        CancellationToken cancellationToken = default,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.BatchSqlQuery(queries, commandTimeoutSeconds, maxRows, maxResponseBytes, cancellationToken, databaseName));
}
