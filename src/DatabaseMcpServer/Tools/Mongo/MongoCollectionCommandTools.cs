using System.ComponentModel;
using System.Text.Json;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DatabaseMcpServer.Tools.Command;

[McpServerToolType]
internal sealed class MongoCollectionCommandTools(MongoCollectionService service)
{
    [ToolPolicy(RequiresConfirmation = true)]
    [McpServerTool(Destructive = true, UseStructuredContent = true, OutputSchemaType = typeof(MongoCollectionResult))]
    [Description("Insert one MongoDB document using a JSON object / Extended JSON v2. Generates ObjectId when _id is absent; returns Canonical EJSON insertedId and writeOutcome. Requires write access and acknowledged write concern (not w=0). Timeout/cancellation may leave committed writes; verify before retrying. No application retries.")]
    public async Task<CallToolResult> MongoInsertOne(
        [Description("Collection name; system.* prohibited.")] string collection,
        [Description("Required document object, at most 4 MiB total JSON input. Preserve BSON types with EJSON v2 wrappers.")] Dictionary<string, JsonElement> document,
        [Description("Full tool JSON UTF-8 budget; minimum 1024. ID must fit before insertion.")] int? maxResponseBytes = null,
        [Description("Configured connection name; omitted captures current connection.")] string? databaseName = null,
        [Description("Operation deadline in seconds; default 30.")] int? commandTimeoutSeconds = null,
        CancellationToken cancellationToken = default)
        => Adapt(await service.ExecuteAsync(new(MongoOperation.Insert, collection, Document: document, MaxResponseBytes: maxResponseBytes,
            DatabaseName: databaseName, CommandTimeoutSeconds: commandTimeoutSeconds), cancellationToken));

    [ToolPolicy(RequiresConfirmation = true)]
    [McpServerTool(Destructive = true, UseStructuredContent = true, OutputSchemaType = typeof(MongoCollectionResult))]
    [Description("Update MongoDB documents; defaults to UpdateOne. Returns matched/modified counts and upsertedId in Canonical EJSON. Requires write access; many requires enableDangerousOperations. Empty filter also requires allowAll. Nonempty filters can still match every document. Multi-document writes are not atomic; timeout may partially apply. Verify unknown writeOutcome before retrying.")]
    public async Task<CallToolResult> MongoUpdate(
        [Description("Collection name.")] string collection,
        [Description("Required filter object. {} requires allowAll and enableDangerousOperations.")] Dictionary<string, JsonElement> filter,
        [Description("Nonempty operator object: $set/$unset/$inc/$mul/$min/$max/$push/$pull/$addToSet/$pop/$currentDate/$setOnInsert. Cannot update _id; no replacement or pipeline.")] Dictionary<string, JsonElement> update,
        [Description("Update all matches; requires enableDangerousOperations. Default false.")] bool many = false,
        [Description("Insert if unmatched; not supported with many=true.")] bool upsert = false,
        [Description("Explicitly permit empty filter; also requires enableDangerousOperations.")] bool allowAll = false,
        [Description("Full tool JSON UTF-8 budget; minimum 1024.")] int? maxResponseBytes = null,
        [Description("Configured connection name; omitted captures current connection.")] string? databaseName = null,
        [Description("Operation deadline in seconds; default 30.")] int? commandTimeoutSeconds = null,
        CancellationToken cancellationToken = default)
        => Adapt(await service.ExecuteAsync(new(MongoOperation.Update, collection, filter, Update: update, Many: many, Upsert: upsert, AllowAll: allowAll,
            MaxResponseBytes: maxResponseBytes, DatabaseName: databaseName, CommandTimeoutSeconds: commandTimeoutSeconds), cancellationToken));

    [ToolPolicy(RequiresConfirmation = true)]
    [McpServerTool(Destructive = true, UseStructuredContent = true, OutputSchemaType = typeof(MongoCollectionResult))]
    [Description("Delete MongoDB documents; defaults to DeleteOne. Requires write access; many requires enableDangerousOperations. Empty filter also requires allowAll. Nonempty filters can still match every document. Returns deletedCount and writeOutcome; multi-document operations may partially apply on timeout. Cancellation does not roll back committed writes.")]
    public async Task<CallToolResult> MongoDelete(
        [Description("Collection name.")] string collection,
        [Description("Required filter object. {} requires allowAll and enableDangerousOperations.")] Dictionary<string, JsonElement> filter,
        [Description("Delete all matches; requires enableDangerousOperations. Default false.")] bool many = false,
        [Description("Explicitly permit empty filter; also requires enableDangerousOperations.")] bool allowAll = false,
        [Description("Full tool JSON UTF-8 budget; minimum 1024.")] int? maxResponseBytes = null,
        [Description("Configured connection name; omitted captures current connection.")] string? databaseName = null,
        [Description("Operation deadline in seconds; default 30.")] int? commandTimeoutSeconds = null,
        CancellationToken cancellationToken = default)
        => Adapt(await service.ExecuteAsync(new(MongoOperation.Delete, collection, filter, Many: many, AllowAll: allowAll,
            MaxResponseBytes: maxResponseBytes, DatabaseName: databaseName, CommandTimeoutSeconds: commandTimeoutSeconds), cancellationToken));
    private static CallToolResult Adapt(MongoCollectionResult result)
        => McpResultAdapter.Structured(result, result.Success);
}
