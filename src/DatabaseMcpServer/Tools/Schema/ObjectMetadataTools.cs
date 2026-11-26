using System.ComponentModel;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DatabaseMcpServer.Tools.Management;

[ToolGroup("schema", "sql", DatabaseToolAccess.Read)]
[McpServerToolType]
internal sealed class ObjectMetadataTools(ObjectMetadataService metadata)
{
    [ToolPolicy(Analysis = true)]
    [McpServerTool(Title = "List Database Objects", ReadOnly = true, UseStructuredContent = true, OutputSchemaType = typeof(ObjectListResult))]
    [Description("List tables, views, routines or triggers. MySQL/SQL Server/PostgreSQL/SQLite use native keyset paging; other SQL providers use SqlSugar metadata with client-side filtering/paging and report pagingMode. Visibility depends on database permissions. Pass the returned identity to get_object_definition where supported. Cursor is bound to filters, configuration content/path, working directory and database; it supports cross-process continuation without a cross-page snapshot.")]
    public Task<CallToolResult> ListObjects(
        [Description("table, view, procedure, function or trigger; SQLite supports table/view/trigger.")] string kind,
        [Description("Optional connection name.")] string? databaseName = null,
        [Description("Exact schema; SQLite supports main only.")] string? schema = null,
        [Description("Literal name prefix, not a LIKE pattern.")] string? namePrefix = null,
        [Description("Exact parent table name, for triggers.")] string? tableName = null,
        [Description("Opaque nextCursor with identical configuration content/path, working directory, database and query conditions. Supports continuation across CLI processes; pre-upgrade cursors must be restarted.")] string? cursor = null,
        [Description("Page size 1-1000, subject to server row cap.")] int limit = 100,
        [Description("Maximum tool JSON UTF-8 bytes. Oversized pages fail; reduce limit.")] int? maxResponseBytes = null,
        [Description("Command timeout in seconds, subject to server cap.")] int? commandTimeoutSeconds = null,
        CancellationToken cancellationToken = default)
        => McpResultAdapter.Structured(metadata.ListObjects(kind, databaseName, schema, namePrefix, tableName, cursor, limit, maxResponseBytes, commandTimeoutSeconds, cancellationToken));

    [ToolPolicy(Analysis = true, Capability = DatabaseMcpServer.Helpers.DatabaseCapability.NativeMetadata)]
    [McpServerTool(Title = "Get Object Definition", ReadOnly = true, UseStructuredContent = true, OutputSchemaType = typeof(ObjectDefinitionResult))]
    [Description("Read an object's native definition. Use identity from list_objects as name (including PostgreSQL overload arguments or table.trigger). definition-unavailable can mean absent, encrypted, invisible or unsupported definition; it does not assert absence. SQL Server/PostgreSQL table DDL reconstruction is not provided.")]
    public Task<CallToolResult> GetObjectDefinition(
        [Description("Object kind from list_objects.")] string kind,
        [Description("Exact identity from list_objects; MySQL SHOW CREATE uses quoted identifiers, other providers bind names as parameters.")] string name,
        [Description("Exact schema; defaults dbo/public/main for SQL Server/PostgreSQL/SQLite.")] string? schema = null,
        [Description("Optional connection name.")] string? databaseName = null,
        [Description("Maximum tool JSON UTF-8 bytes; oversized definitions fail.")] int? maxResponseBytes = null,
        [Description("Command timeout in seconds.")] int? commandTimeoutSeconds = null,
        CancellationToken cancellationToken = default)
        => McpResultAdapter.Structured(metadata.GetDefinition(kind, name, schema, databaseName, maxResponseBytes, commandTimeoutSeconds, cancellationToken));
}
