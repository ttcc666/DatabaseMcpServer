using System.ComponentModel;
using System.Text.Json;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DatabaseMcpServer.Tools.Command;

[ToolGroup("mongo", "mongo", DatabaseMcpServer.Helpers.DatabaseToolAccess.Write)]
[McpServerToolType]
internal sealed class MongoCollectionCommandTools(MongoCollectionService service)
{
    [ToolPolicy(RequiresConfirmation = true)]
    [McpServerTool(Title = "Insert MongoDB Documents", Destructive = true, UseStructuredContent = true, OutputSchemaType = typeof(MongoCollectionResult))]
    [Description("Insert one or more MongoDB documents via SqlSugar.MongoDbCore ADO insertMany using JSON / Extended JSON v2 objects (1-1000 documents, <= 4 MiB total). Requires write access and acknowledged write concern (not w=0).")]
    public async Task<CallToolResult> MongoInsertMany(
        [Description("Collection name; system.* prohibited.")] string collection,
        [Description("1-1000 document objects, JSON / Extended JSON v2, <= 4 MiB total input.")] Dictionary<string, JsonElement>[] documents,
        [Description("Full tool JSON UTF-8 budget; minimum 1024.")] int? maxResponseBytes = null,
        [Description("Configured connection name; omitted captures current connection.")] string? databaseName = null,
        [Description("Operation deadline in seconds; default 30.")] int? commandTimeoutSeconds = null,
        CancellationToken cancellationToken = default)
        => Adapt(await service.ExecuteAsync(new(MongoOperation.InsertMany, collection, Documents: documents, MaxResponseBytes: maxResponseBytes,
            DatabaseName: databaseName, CommandTimeoutSeconds: commandTimeoutSeconds), cancellationToken));

    [ToolPolicy(RequiresConfirmation = true)]
    [McpServerTool(Title = "Update MongoDB Documents", Destructive = true, UseStructuredContent = true, OutputSchemaType = typeof(MongoCollectionResult))]
    [Description("Batch update MongoDB documents via SqlSugar.MongoDbCore ADO updateMany. Requires write access and enableDangerousOperations. Empty filter also requires allowAll. Returns affected count and writeOutcome.")]
    public async Task<CallToolResult> MongoUpdate(
        [Description("Collection name.")] string collection,
        [Description("Required filter object. {} requires allowAll.")] Dictionary<string, JsonElement> filter,
        [Description("Nonempty operator object: $set/$unset/$inc/$mul/$min/$max/$push/$pull/$addToSet/$pop/$currentDate/$setOnInsert. Cannot update _id; no replacement or pipeline.")] Dictionary<string, JsonElement> update,
        [Description("Explicitly permit empty filter; also requires enableDangerousOperations.")] bool allowAll = false,
        [Description("Full tool JSON UTF-8 budget; minimum 1024.")] int? maxResponseBytes = null,
        [Description("Configured connection name; omitted captures current connection.")] string? databaseName = null,
        [Description("Operation deadline in seconds; default 30.")] int? commandTimeoutSeconds = null,
        CancellationToken cancellationToken = default)
        => Adapt(await service.ExecuteAsync(new(MongoOperation.Update, collection, filter, Update: update, AllowAll: allowAll,
            MaxResponseBytes: maxResponseBytes, DatabaseName: databaseName, CommandTimeoutSeconds: commandTimeoutSeconds), cancellationToken));

    [ToolPolicy(RequiresConfirmation = true)]
    [McpServerTool(Title = "Delete MongoDB Documents", Destructive = true, UseStructuredContent = true, OutputSchemaType = typeof(MongoCollectionResult))]
    [Description("Batch delete MongoDB documents via SqlSugar.MongoDbCore ADO deleteMany. Requires write access and enableDangerousOperations. Empty filter also requires allowAll. Returns deletedCount and writeOutcome.")]
    public async Task<CallToolResult> MongoDelete(
        [Description("Collection name.")] string collection,
        [Description("Required filter object. {} requires allowAll.")] Dictionary<string, JsonElement> filter,
        [Description("Explicitly permit empty filter; also requires enableDangerousOperations.")] bool allowAll = false,
        [Description("Full tool JSON UTF-8 budget; minimum 1024.")] int? maxResponseBytes = null,
        [Description("Configured connection name; omitted captures current connection.")] string? databaseName = null,
        [Description("Operation deadline in seconds; default 30.")] int? commandTimeoutSeconds = null,
        CancellationToken cancellationToken = default)
        => Adapt(await service.ExecuteAsync(new(MongoOperation.Delete, collection, filter, AllowAll: allowAll,
            MaxResponseBytes: maxResponseBytes, DatabaseName: databaseName, CommandTimeoutSeconds: commandTimeoutSeconds), cancellationToken));

    [ToolPolicy(RequiresConfirmation = true, Access = DatabaseMcpServer.Helpers.DatabaseToolAccess.Schema)]
    [McpServerTool(Title = "Truncate MongoDB Collection", Destructive = true, UseStructuredContent = true, OutputSchemaType = typeof(MongoCollectionResult))]
    [Description("Remove all documents in a MongoDB collection via cancellable SqlSugar ADO deleteMany, preserving the collection and indexes. Requires schema-admin access and enableDangerousOperations; cancellation does not roll back writes.")]
    public async Task<CallToolResult> MongoTruncate(
        [Description("Collection name.")] string collection,
        [Description("Full tool JSON UTF-8 budget; minimum 1024.")] int? maxResponseBytes = null,
        [Description("Configured connection name; omitted captures current connection.")] string? databaseName = null,
        [Description("Operation deadline in seconds; default 30.")] int? commandTimeoutSeconds = null,
        CancellationToken cancellationToken = default)
        => Adapt(await service.ExecuteAsync(new(MongoOperation.Truncate, collection, MaxResponseBytes: maxResponseBytes,
            DatabaseName: databaseName, CommandTimeoutSeconds: commandTimeoutSeconds), cancellationToken));

    private static CallToolResult Adapt(MongoCollectionResult result)
        => McpResultAdapter.Structured(result, result.Success);
}
