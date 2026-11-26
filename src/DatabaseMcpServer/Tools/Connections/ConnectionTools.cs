using System.ComponentModel;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Operations;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;

namespace DatabaseMcpServer.Tools.Management;

[ToolGroup("connection", "any", DatabaseMcpServer.Helpers.DatabaseToolAccess.Read)]
[McpServerToolType]
internal sealed class ConnectionTools(ConnectionOperations operations)
{
    [ToolPolicy(Analysis = true)]
    [McpServerTool(Title = "Get Database Capabilities", ReadOnly = true, UseStructuredContent = true, OutputSchemaType = typeof(DatabaseCapabilitiesResult))]
    [Description("Inspect configured provider capabilities, budgets and settings without connecting to the database. No credentials are returned.")]
    public Task<CallToolResult> GetDatabaseCapabilities(
        [Description("Optional database connection name. Does not change current database.")] string? databaseName = null,
        CancellationToken cancellationToken = default)
        => McpResultAdapter.Structured(operations.GetDatabaseCapabilities(databaseName, cancellationToken));

    [ToolPolicy(Analysis = true)]
    [McpServerTool(Title = "Test Connection", ReadOnly = true, UseStructuredContent = true, OutputSchemaType = typeof(ConnectionTestResult))]
    [Description("Test a named or current connection with a fixed target snapshot, optional transient retries and a total deadline. Default: no retries. Backoff releases the execution slot.")]
    public Task<CallToolResult> TestConnection(CancellationToken cancellationToken = default,
        [Description("Optional connection name; the target stays fixed across retries.")] string? databaseName = null,
        [Description("Retries after a transient connection failure, 0-10; default 0.")] int maxRetries = 0,
        [Description("Initial exponential backoff in milliseconds, 1-60000.")] int initialDelayMs = 1000,
        [Description("Total deadline including admission, connection and backoff, 1-300 seconds; default 30. Mongo's initial synchronous provider query may delay cancellation; cleanup is awaited.")] int totalTimeoutSeconds = 30)
        => McpResultAdapter.Structured(operations.TestConnection(cancellationToken, databaseName, maxRetries, initialDelayMs, totalTimeoutSeconds));

    [McpServerTool(Title = "Get Database Config", ReadOnly = true)]
    [Description("Summarize DB_CONFIG_PATH or DB_CONNECTION_STRING/DB_TYPE and return the active connection name, description, database type, masked connection string, and mode metadata.")]
    public string GetDatabaseConfig()
        => McpResultAdapter.Text(operations.GetDatabaseConfig());

    [McpServerTool(Title = "Validate Configuration", ReadOnly = true)]
    [Description("Verify whether the environment variables or config file can produce a usable connection and return success/configured/currentDatabase/databaseType/message fields describing the outcome.")]
    public string ValidateConfiguration()
        => McpResultAdapter.Text(operations.ValidateConfiguration());

    [McpServerTool(Title = "Reload Database Config", Destructive = true, Idempotent = true)]
    [Description("Reload the databases.json file from DB_CONFIG_PATH, update connection admission state, and return the applied currentDatabase plus whether the previous selection was preserved. In CLI tool mode, the preserved selection is also persisted for later invocations.")]
    public string ReloadDatabaseConfig()
        => McpResultAdapter.Text(operations.ReloadDatabaseConfig());

    [ToolPolicy(Analysis = true)]
    [McpServerTool(Title = "List Databases", ReadOnly = true, UseStructuredContent = true, OutputSchemaType = typeof(ConnectionListResult))]
    [Description("List every configured database connection (name, type, description, default flag, current flag) so callers can choose a target.")]
    public CallToolResult ListDatabases()
        => McpResultAdapter.Structured(operations.ListDatabases());

    [McpServerTool(Title = "Switch Database", Destructive = true, Idempotent = true)]
    [Description("Switch the active connection to databaseName and return previousDatabase/currentDatabase; throw an error if the name does not exist. In CLI tool mode, the selected connection is persisted per resolved config path for later invocations.")]
    public string SwitchDatabase([Description("Database connection name to switch to")] string databaseName)
        => McpResultAdapter.Text(operations.SwitchDatabase(databaseName));

    [McpServerTool(Title = "Get Current Database", ReadOnly = true)]
    [Description("Return the currently active database connection name and database type so callers know the execution context. In CLI tool mode, this reflects the persisted current selection for the resolved config path.")]
    public string GetCurrentDatabase()
        => McpResultAdapter.Text(operations.GetCurrentDatabase());

    [McpServerTool(Title = "Health Check", ReadOnly = true)]
    [Description("Perform a comprehensive health check on all configured database connections, testing connectivity and response time for each.")]
    public Task<string> HealthCheck(CancellationToken cancellationToken = default,
        IProgress<ModelContextProtocol.ProgressNotificationValue>? progress = null)
        => McpResultAdapter.Text(operations.HealthCheck(cancellationToken, progress == null ? null : (done, total, message) =>
            progress.Report(new ModelContextProtocol.ProgressNotificationValue { Progress = done, Total = total, Message = message })));
}
