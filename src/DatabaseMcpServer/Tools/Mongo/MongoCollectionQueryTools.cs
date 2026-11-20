using System.ComponentModel;
using System.Text.Json;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DatabaseMcpServer.Tools.Query;

[McpServerToolType]
internal sealed class MongoCollectionQueryTools(MongoCollectionService service)
{
    // Optional object parameters use null only as the omitted-value sentinel.
    // Non-null annotations publish object-only schemas; explicit JSON null is rejected before binding.
    [McpServerTool(ReadOnly = true, UseStructuredContent = true, OutputSchemaType = typeof(MongoCollectionResult))]
    [Description("List MongoDB collections (name/type) within row and UTF-8 budgets. Requires a MongoDb connection with an explicit database in its URI. limitReached with truncated=null means a row ceiling, not proof of more results.")]
    public async Task<CallToolResult> MongoListCollections(
        [Description("Optional literal collection name prefix.")] string? namePrefix = null,
        [Description("Maximum results; default 1000, subject to server policy.")] int? maxRows = null,
        [Description("Full tool JSON UTF-8 budget; minimum 1024, default 1 MiB.")] int? maxResponseBytes = null,
        [Description("Configured connection name; omitted captures current connection.")] string? databaseName = null,
        [Description("Whole operation deadline in seconds; default 30. 0 requires server permission.")] int? commandTimeoutSeconds = null,
        CancellationToken cancellationToken = default)
        => Adapt(await service.ExecuteAsync(new(MongoOperation.List, NamePrefix: namePrefix, MaxRows: maxRows,
            MaxResponseBytes: maxResponseBytes, DatabaseName: databaseName, CommandTimeoutSeconds: commandTimeoutSeconds), cancellationToken));

    [McpServerTool(ReadOnly = true, UseStructuredContent = true, OutputSchemaType = typeof(MongoCollectionResult))]
    [Description("Find MongoDB documents using JSON objects / Extended JSON v2; returns Canonical Extended JSON preserving BSON types. No JavaScript or aggregation. Stable sort appends _id; concurrent writes can move pages. limitReached with truncated=null means row ceiling; truncated=true means byte ceiling. Use mongo_count separately for totals.")]
    public async Task<CallToolResult> MongoFind(
        [Description("Collection name; system.* collections are prohibited.")] string collection,
        [Description("Query object; omit for {}. Explicit null is invalid. Use {$oid:...} for ObjectId; ordinary strings remain strings.")] Dictionary<string, JsonElement> filter = null!,
        [Description("Projection object with 0/1 values; omit for all fields. Cannot mix inclusion/exclusion except _id.")] Dictionary<string, JsonElement> projection = null!,
        [Description("Sort object with 1/-1 values; omit for {_id:1}. _id is appended as tie breaker.")] Dictionary<string, JsonElement> sort = null!,
        [Description("Documents to skip, 0–100000; deep offsets are expensive. Prefer range filters on indexed keys.")] int skip = 0,
        [Description("Maximum documents; default 1000, subject to server policy.")] int? maxRows = null,
        [Description("Full tool JSON UTF-8 budget; minimum 1024, default 1 MiB.")] int? maxResponseBytes = null,
        [Description("Configured connection name; omitted captures current connection.")] string? databaseName = null,
        [Description("Whole cursor deadline in seconds; default 30. 0 requires server permission.")] int? commandTimeoutSeconds = null,
        CancellationToken cancellationToken = default)
        => Adapt(await service.ExecuteAsync(new(MongoOperation.Find, collection, filter, projection, sort, Skip: skip, MaxRows: maxRows,
            MaxResponseBytes: maxResponseBytes, DatabaseName: databaseName, CommandTimeoutSeconds: commandTimeoutSeconds), cancellationToken));

    [McpServerTool(ReadOnly = true, UseStructuredContent = true, OutputSchemaType = typeof(MongoCollectionResult))]
    [Description("Count MongoDB documents matching a JSON / Extended JSON v2 filter. Returns exact matchedCount, subject to operation timeout; does not fetch documents.")]
    public async Task<CallToolResult> MongoCount(
        [Description("Collection name.")] string collection,
        [Description("Query object; omit for {}. Explicit null and JavaScript operators are prohibited.")] Dictionary<string, JsonElement> filter = null!,
        [Description("Full tool JSON UTF-8 budget; minimum 1024.")] int? maxResponseBytes = null,
        [Description("Configured connection name; omitted captures current connection.")] string? databaseName = null,
        [Description("Operation deadline in seconds; default 30.")] int? commandTimeoutSeconds = null,
        CancellationToken cancellationToken = default)
        => Adapt(await service.ExecuteAsync(new(MongoOperation.Count, collection, filter, MaxResponseBytes: maxResponseBytes,
            DatabaseName: databaseName, CommandTimeoutSeconds: commandTimeoutSeconds), cancellationToken));
    private static CallToolResult Adapt(MongoCollectionResult result)
        => McpResultAdapter.Structured(result, result.Success);
}
