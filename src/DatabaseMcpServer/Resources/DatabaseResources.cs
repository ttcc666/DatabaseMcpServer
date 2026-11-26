using System.ComponentModel;
using DatabaseMcpServer.Operations;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DatabaseMcpServer.Resources;

[McpServerResourceType]
internal sealed class DatabaseResources(
    ConnectionOperations connections,
    SchemaReadOperations schema)
{
    [McpServerResource(UriTemplate = "db://connections", Name = "database_connections", Title = "Database Connections", MimeType = "application/json")]
    [Description("Configured database connections (excluding secrets) and current active selection.")]
    public TextResourceContents GetConnections()
    {
        return new TextResourceContents
        {
            Uri = "db://connections",
            MimeType = "application/json",
            Text = DatabaseMcpServer.Tools.McpResultAdapter.Text(connections.ListDatabases())
        };
    }

    [McpServerResource(UriTemplate = "db://capabilities/{databaseName}", Name = "database_capabilities", Title = "Database Capabilities", MimeType = "application/json")]
    [Description("Offline provider capabilities, budgets, and optimization settings for a configured connection.")]
    public async Task<TextResourceContents> GetCapabilities(string databaseName, CancellationToken cancellationToken)
    {
        var result = await connections.GetDatabaseCapabilities(databaseName, cancellationToken).ConfigureAwait(false);
        return new TextResourceContents
        {
            Uri = $"db://capabilities/{Uri.EscapeDataString(databaseName)}",
            MimeType = "application/json",
            Text = DatabaseMcpServer.Tools.McpResultAdapter.Text(result)
        };
    }

    [McpServerResource(UriTemplate = "db://schema/{databaseName}/{tableName}", Name = "table_schema", Title = "Table Schema", MimeType = "application/json")]
    [Description("Cached schema metadata (columns, primary keys, identity columns, indexes) for a SQL table.")]
    public async Task<TextResourceContents> GetTableSchema(string databaseName, string tableName, CancellationToken cancellationToken)
    {
        var result = await schema.GetTableSchema(tableName, databaseName, cancellationToken: cancellationToken).ConfigureAwait(false);
        return new TextResourceContents
        {
            Uri = $"db://schema/{Uri.EscapeDataString(databaseName)}/{Uri.EscapeDataString(tableName)}",
            MimeType = "application/json",
            Text = DatabaseMcpServer.Tools.McpResultAdapter.Text(result)
        };
    }
}
