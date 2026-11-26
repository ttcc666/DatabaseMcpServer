using System.ComponentModel;
using System.Text.Json;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DatabaseMcpServer.Tools.Query;

[ToolGroup("mongo", "mongo", DatabaseMcpServer.Helpers.DatabaseToolAccess.Read)]
[McpServerToolType]
internal sealed class MongoCollectionQueryTools(MongoCollectionService service)
{
    [ToolPolicy(Analysis = true)]
    [McpServerTool(Title = "Find MongoDB Documents", ReadOnly = true, UseStructuredContent = true, OutputSchemaType = typeof(MongoCollectionResult))]
    [Description("Find MongoDB documents via SqlSugar's built-in ADO using JSON objects / Extended JSON v2 input. Output uses provider-converted JSON: ObjectId becomes string, missing fields become null; nested BSON uses Extended JSON. Not a lossless BSON round trip. Stable sort appends _id; concurrent writes can move pages. limitReached with truncated=null means row ceiling; truncated=true means byte ceiling.")]
    public async Task<CallToolResult> MongoFind(
        [Description("Collection name; system.* collections are prohibited.")] string collection,
        [Description("Query object; omit for {}. Explicit null is invalid. Use {$oid:...} for ObjectId; ordinary strings remain strings.")] Dictionary<string, JsonElement> filter = null!,
        [Description("Projection object with 0/1 values; omit for all fields. Cannot mix inclusion/exclusion except _id.")] Dictionary<string, JsonElement> projection = null!,
        [Description("Sort object with 1/-1 values; omit for {_id:1}. _id is appended as tie breaker.")] Dictionary<string, JsonElement> sort = null!,
        [Description("Documents to skip, 0–100000; deep offsets are expensive. Prefer range filters on indexed keys.")] int skip = 0,
        [Description("Maximum documents; default 1000, subject to server policy.")] int? maxRows = null,
        [Description("Full tool JSON UTF-8 budget; minimum 1024, default 1 MiB. Checked after provider materialization; does not bound read memory.")] int? maxResponseBytes = null,
        [Description("Configured connection name; omitted captures current connection.")] string? databaseName = null,
        [Description("Request deadline in seconds; default 30. Initial synchronous provider query may delay cancellation; cleanup is awaited. 0 requires server permission.")] int? commandTimeoutSeconds = null,
        CancellationToken cancellationToken = default)
        => Adapt(await service.ExecuteAsync(new(MongoOperation.Find, collection, filter, projection, sort, Skip: skip, MaxRows: maxRows,
            MaxResponseBytes: maxResponseBytes, DatabaseName: databaseName, CommandTimeoutSeconds: commandTimeoutSeconds), cancellationToken));

    [ToolPolicy(Analysis = true)]
    [McpServerTool(Title = "Aggregate MongoDB Collection", ReadOnly = true, UseStructuredContent = true, OutputSchemaType = typeof(MongoCollectionResult))]
    [Description("Run a read-only Mongo aggregation via SqlSugar's built-in ADO with a server row limit and response byte budget after provider materialization. Output uses provider-converted JSON, not lossless BSON; nested BSON uses Extended JSON. Supports match/project/group/sort and same-database $lookup/$facet. Nested pipelines share 64 stages, at most 4 levels and 8 facet branches; no nested facet inside facet, writes or JavaScript.")]
    public async Task<CallToolResult> MongoAggregate(
        [Description("Collection name.")] string collection,
        [Description("1-64 single-operator stage objects, JSON/Extended JSON v2; total input <=4 MiB.")] Dictionary<string, JsonElement>[] pipeline,
        [Description("Configured connection name.")] string? databaseName = null,
        [Description("Maximum returned documents, subject to server cap.")] int? maxRows = null,
        [Description("Maximum tool JSON UTF-8 bytes, minimum 1024. Checked after provider materialization; does not bound read memory.")] int? maxResponseBytes = null,
        [Description("Request deadline in seconds, default 30. Initial synchronous provider query may delay cancellation; cleanup is awaited.")] int? commandTimeoutSeconds = null,
        CancellationToken cancellationToken = default)
        => Adapt(await service.ExecuteAsync(new(MongoOperation.Aggregate, collection, Pipeline: pipeline, DatabaseName: databaseName,
            MaxRows: maxRows, MaxResponseBytes: maxResponseBytes, CommandTimeoutSeconds: commandTimeoutSeconds), cancellationToken));

    private static CallToolResult Adapt(MongoCollectionResult result)
        => McpResultAdapter.Structured(result, result.Success);
}
