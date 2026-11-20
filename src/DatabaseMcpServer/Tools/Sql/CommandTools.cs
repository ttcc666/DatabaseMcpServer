using System.ComponentModel;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Operations;
using ModelContextProtocol.Server;

namespace DatabaseMcpServer.Tools.Command;

[McpServerToolType]
internal sealed class CommandTools(SqlCommandOperations operations)
{
    [ToolPolicy(RequiresConfirmation = true)]
    [McpServerTool(Destructive = true)]
    [Description("Execute INSERT/UPDATE/DELETE SQL after dangerous-operation detection, binding typed parameters, and return affectedRows.")]
    public Task<string> ExecuteCommand(
        [Description("SQL command to execute")] string sql,
        [Description("Typed input parameter array. NULL requires dbType.")] DatabaseParameter[]? parameters = null,
        [Description("Optional SQL command timeout in seconds. Omit to use the provider default (typically 300). 0 means wait indefinitely.")] int? commandTimeoutSeconds = null,
        CancellationToken cancellationToken = default,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.ExecuteCommand(sql, parameters, commandTimeoutSeconds, cancellationToken, databaseName));

    [ToolPolicy(RequiresConfirmation = true)]
    [McpServerTool(Destructive = true)]
    [Description("Invoke a stored procedure with typed Input, Output, InputOutput or ReturnValue parameters; return rows and outputParameters. Output directions support SQL Server/MySQL; ReturnValue supports SQL Server.")]
    public string CallStoredProcedure(
        [Description("Stored procedure name")] string procedureName,
        [Description("Typed input/output parameter array. NULL and output parameters require dbType; string/binary outputs require size.")] DatabaseParameter[]? parameters = null,
        [Description("Optional SQL command timeout in seconds. Omit to use the provider default (typically 300). 0 means wait indefinitely.")] int? commandTimeoutSeconds = null,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null,
        [Description("Maximum tool JSON bytes. Oversized procedure results fail after execution; side effects are not rolled back.")] int? maxResponseBytes = null)
        => McpResultAdapter.Text(operations.CallStoredProcedure(procedureName, parameters, commandTimeoutSeconds, databaseName, maxResponseBytes));

    [ToolPolicy(RequiresConfirmation = true)]
    [McpServerTool(Destructive = true)]
    [Description("Execute a SQL Server script that contains GO batches, automatically splitting the script and returning total affectedRows.")]
    public string ExecuteCommandWithGo(
        [Description("SQL script containing GO statements")] string sql,
        [Description("Optional SQL command timeout in seconds. Omit to use the provider default (typically 300). 0 means wait indefinitely.")] int? commandTimeoutSeconds = null,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.ExecuteCommandWithGo(sql, commandTimeoutSeconds, databaseName));

    [ToolPolicy(RequiresConfirmation = true)]
    [McpServerTool(Destructive = true)]
    [Description("Execute command objects sequentially over one connection; each object contains sql and optional typed parameters. Return success, affectedRows or error per command. Commands are not transactional; earlier writes remain committed on later failure or cancellation.")]
    public Task<string> BatchExecuteCommands(
        [Description("Nonempty array of {sql, parameters?} command objects. Each parameters value is a typed parameter array.")] DatabaseCommand[] commands,
        [Description("Optional SQL command timeout in seconds applied to every command in the batch. Omit to use the provider default (typically 300). 0 means wait indefinitely.")] int? commandTimeoutSeconds = null,
        CancellationToken cancellationToken = default,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.BatchExecuteCommands(commands, commandTimeoutSeconds, cancellationToken, databaseName));
}
