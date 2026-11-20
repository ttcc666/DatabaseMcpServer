using System.ComponentModel;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Operations;
using ModelContextProtocol.Server;

namespace DatabaseMcpServer.Tools.Management;

[McpServerToolType]
internal sealed class ConnectionTools(ConnectionOperations operations)
{
    [McpServerTool(ReadOnly = true)]
    [Description("Inspect provider capabilities, execution budgets, effective driver/SqlSugar settings and advisory optimization options. Does not open a database connection or disclose credentials. Choose a named connection before querying.")]
    public string GetDatabaseCapabilities(
        [Description("Optional database connection name. Does not change current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.GetDatabaseCapabilities(databaseName));

    [McpServerTool(ReadOnly = true)]
    [Description("Validate the currently active connection and return success, connected, currentDatabase, and databaseType so callers can confirm the session is healthy.")]
    public Task<string> TestConnection(CancellationToken cancellationToken = default)
        => McpResultAdapter.Text(operations.TestConnection(cancellationToken));

    [McpServerTool(ReadOnly = true)]
    [Description("Create and validate a connection using databaseName, then return success, connected, and databaseName to prove that specific entry is healthy.")]
    public Task<string> TestConnectionByName([Description("Database connection name")] string databaseName, CancellationToken cancellationToken = default)
        => McpResultAdapter.Text(operations.TestConnectionByName(databaseName, cancellationToken));

    [McpServerTool(ReadOnly = true)]
    [Description("Summarize DB_CONFIG_PATH or DB_CONNECTION_STRING/DB_TYPE and return the active connection name, description, database type, masked connection string, and mode metadata.")]
    public string GetDatabaseConfig()
        => McpResultAdapter.Text(operations.GetDatabaseConfig());

    [McpServerTool(ReadOnly = true)]
    [Description("Verify whether the environment variables or config file can produce a usable connection and return success/configured/currentDatabase/databaseType/message fields describing the outcome.")]
    public string ValidateConfiguration()
        => McpResultAdapter.Text(operations.ValidateConfiguration());

    [McpServerTool(Idempotent = true)]
    [Description("Reload the databases.json file from DB_CONFIG_PATH, update connection admission state, and return the applied currentDatabase plus whether the previous selection was preserved. In CLI tool mode, the preserved selection is also persisted for later invocations.")]
    public string ReloadDatabaseConfig()
        => McpResultAdapter.Text(operations.ReloadDatabaseConfig());

    [McpServerTool(ReadOnly = true)]
    [Description("List every configured database connection (name, type, description, default flag, current flag) so callers can choose a target.")]
    public string ListDatabases()
        => McpResultAdapter.Text(operations.ListDatabases());

    [McpServerTool(Idempotent = true)]
    [Description("Switch the active connection to databaseName and return previousDatabase/currentDatabase; throw an error if the name does not exist. In CLI tool mode, the selected connection is persisted per resolved config path for later invocations.")]
    public string SwitchDatabase([Description("Database connection name to switch to")] string databaseName)
        => McpResultAdapter.Text(operations.SwitchDatabase(databaseName));

    [McpServerTool(ReadOnly = true)]
    [Description("Return the currently active database connection name and database type so callers know the execution context. In CLI tool mode, this reflects the persisted current selection for the resolved config path.")]
    public string GetCurrentDatabase()
        => McpResultAdapter.Text(operations.GetCurrentDatabase());

    [McpServerTool(ReadOnly = true)]
    [Description("Perform a comprehensive health check on all configured database connections, testing connectivity and response time for each.")]
    public Task<string> HealthCheck(CancellationToken cancellationToken = default)
        => McpResultAdapter.Text(operations.HealthCheck(cancellationToken));

    [McpServerTool(ReadOnly = true)]
    [Description("Test connection with automatic retry mechanism. Attempts to reconnect up to maxRetries times with exponential backoff.")]
    public Task<string> TestConnectionWithRetry(
        [Description("Maximum number of retry attempts (default: 3)")] int maxRetries = 3,
        [Description("Initial retry delay in milliseconds (default: 1000)")] int initialDelayMs = 1000,
        CancellationToken cancellationToken = default)
        => McpResultAdapter.Text(operations.TestConnectionWithRetry(maxRetries, initialDelayMs, cancellationToken));
}
