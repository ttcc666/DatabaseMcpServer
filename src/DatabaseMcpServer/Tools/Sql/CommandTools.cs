using System.ComponentModel;
using System.Text.Json;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Operations;
using ModelContextProtocol.Server;

namespace DatabaseMcpServer.Tools.Command;

[ToolGroup("command", "sql", DatabaseMcpServer.Helpers.DatabaseToolAccess.Write)]
[McpServerToolType]
internal sealed class CommandTools(SqlCommandOperations operations)
{
    [ToolPolicy(RequiresConfirmation = true, Capability = DatabaseMcpServer.Helpers.DatabaseCapability.InsertIdentity)]
    [McpServerTool(Title = "Insert Row", Destructive = true)]
    [Description("Insert one row using SqlSugar and return its integer identity as a decimal string (Int64). Supports SQL Server/MySQL/SQLite with one SqlSugar-recognized integer identity; SQLite requires AUTOINCREMENT. Defaults, constraints and triggers run in the database. Cancellation does not guarantee rollback; do not retry without checking the outcome.")]
    public Task<string> InsertRow(
        [Description("Unqualified table name; inspect get_table_schema first. Schema-qualified names and ambiguous SQL Server table names are unsupported by this SqlSugar metadata path; use execute_command for those.")] string tableName,
        [Description("Nonempty object of 1-128 case-sensitive column names to string/bool/number/null values, maximum 4 MiB. Omit the identity column; no nested values. Use execute_command for explicit ADO types or binary values.")] JsonElement row,
        [Description("Optional database connection name.")] string? databaseName = null,
        [Description("Total deadline including admission and execution. Synchronous metadata and SQLite cancellation follow SqlSugar/driver limitations.")] int? commandTimeoutSeconds = null,
        CancellationToken cancellationToken = default)
        => McpResultAdapter.Text(operations.InsertRow(tableName, row, databaseName, commandTimeoutSeconds, cancellationToken));

    [ToolPolicy(RequiresConfirmation = true)]
    [McpServerTool(Title = "Execute Command", Destructive = true)]
    [Description("Execute INSERT/UPDATE/DELETE SQL after dangerous-operation detection, binding typed parameters, and return affectedRows.")]
    public Task<string> ExecuteCommand(
        [Description("SQL command to execute")] string sql,
        [Description("Typed input parameter array. NULL requires dbType.")] DatabaseParameter[]? parameters = null,
        [Description("Total deadline including queue and execution; default server query timeout (normally 30 seconds). 0 requires server permission for infinite timeout.")] int? commandTimeoutSeconds = null,
        CancellationToken cancellationToken = default,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.ExecuteCommand(sql, parameters, commandTimeoutSeconds, cancellationToken, databaseName));

    [ToolPolicy(RequiresConfirmation = true, Access = DatabaseMcpServer.Helpers.DatabaseToolAccess.Schema, Capability = DatabaseMcpServer.Helpers.DatabaseCapability.StoredProcedures)]
    [McpServerTool(Title = "Call Stored Procedure", Destructive = true)]
    [Description("Invoke a stored procedure with typed Input, Output, InputOutput or ReturnValue parameters; return rows and outputParameters. Output directions support SQL Server/MySQL; ReturnValue supports SQL Server.")]
    public Task<string> CallStoredProcedure(
        [Description("Stored procedure name")] string procedureName,
        [Description("Typed input/output parameter array. NULL and output parameters require dbType; string/binary outputs require size.")] DatabaseParameter[]? parameters = null,
        [Description("Total deadline including admission, execution and reading; default server query timeout (normally 30 seconds). 0 requires the server's infinite-timeout policy.")] int? commandTimeoutSeconds = null,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null,
        [Description("Maximum tool JSON bytes. Stops reading on budget exhaustion; side effects are not rolled back.")] int? maxResponseBytes = null,
        [Description("Maximum returned rows across procedure result sets. Output parameters require full completion.")] int? maxRows = null,
        CancellationToken cancellationToken = default)
        => McpResultAdapter.Text(operations.CallStoredProcedure(procedureName, parameters, commandTimeoutSeconds, databaseName, maxResponseBytes, maxRows, cancellationToken));

    [ToolPolicy(RequiresConfirmation = true, Access = DatabaseMcpServer.Helpers.DatabaseToolAccess.Schema, Capability = DatabaseMcpServer.Helpers.DatabaseCapability.SqlServerBatches)]
    [McpServerTool(Title = "Execute SQL Server GO Script", Destructive = true)]
    [Description("Execute a SQL Server script that contains GO batches, automatically splitting the script and returning total affectedRows.")]
    public string ExecuteCommandWithGo(
        [Description("SQL script containing GO statements")] string sql,
        [Description("Optional SQL command timeout in seconds. Omit to use the provider default (typically 300). 0 means wait indefinitely.")] int? commandTimeoutSeconds = null,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.ExecuteCommandWithGo(sql, commandTimeoutSeconds, databaseName));

    [ToolPolicy(RequiresConfirmation = true)]
    [McpServerTool(Title = "Batch Execute Commands", Destructive = true)]
    [Description("Execute typed commands sequentially. Default atomic=false preserves per-item success and partial commits. Explicit atomic=true prevalidates ordinary DML and commits all or rolls back; transactionOutcome=unknown requires verification before retry. External trigger side effects and sequences are outside this guarantee.")]
    public Task<string> BatchExecuteCommands(
        [Description("Nonempty array of {sql, parameters?} command objects. Each parameters value is a typed parameter array.")] DatabaseCommand[] commands,
        [Description("For atomic=false: total deadline including queue and all commands, default server query timeout (normally 30 seconds). For atomic=true: per-command timeout; totalTimeoutSeconds controls the transaction deadline. 0 requires server permission for infinite timeout.")] int? commandTimeoutSeconds = null,
        CancellationToken cancellationToken = default,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null,
        [Description("Default false preserves partial commits. True executes 1-100 validated single INSERT/UPDATE/DELETE statements in one transaction on SQLite/SQL Server/PostgreSQL. unknown outcome requires verification before retry.")] bool atomic = false,
        [Description("Whole atomic request deadline, 1-300 seconds and subject to server cap; ignored when atomic=false.")] int totalTimeoutSeconds = 30)
        => McpResultAdapter.Text(operations.BatchExecuteCommands(commands, commandTimeoutSeconds, cancellationToken, databaseName, atomic, totalTimeoutSeconds));
}
