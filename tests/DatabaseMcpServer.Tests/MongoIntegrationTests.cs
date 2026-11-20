using System.Text.Json;
using DatabaseMcpServer.Cli;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using DatabaseMcpServer.Tools.Management;
using DatabaseMcpServer.Web;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Events;
using static DatabaseMcpServer.Tests.MongoDocumentContractTests;
using static DatabaseMcpServer.Tests.MongoCollectionServiceTests;

namespace DatabaseMcpServer.Tests;

public sealed class MongoIntegrationFactAttribute : FactAttribute
{
    public MongoIntegrationFactAttribute()
    { if (Environment.GetEnvironmentVariable("DBMCP_RUN_MONGODB_INTEGRATION") != "1") Skip = "Requires scripts/test-mongodb-integration.ps1."; }
}

[Trait("Category", "MongoIntegration")]
public class MongoIntegrationTests
{
    [MongoIntegrationFact]
    public async Task CollectionTools_RoundTripInsertFindUpdateDeleteAndNativePing()
    {
        using var test = new MongoFixture();
        var inserted = await test.Call(new(MongoOperation.Insert, "orders", Document: Object("""{"customer":"甲","amount":{"$numberDecimal":"12.3456789"},"large":{"$numberLong":"9007199254740993"}}""")));
        var id = inserted.GetProperty("insertedId");
        Assert.Equal(24, id.GetProperty("$oid").GetString()!.Length);
        Assert.Equal("acknowledged", inserted.GetProperty("writeOutcome").GetString());
        var filter = Object("{\"_id\":" + id.GetRawText() + "}");
        var found = await test.Call(new(MongoOperation.Find, "orders", filter));
        Assert.Equal("甲", found.GetProperty("documents")[0].GetProperty("customer").GetString());
        Assert.Equal("12.3456789", found.GetProperty("documents")[0].GetProperty("amount").GetProperty("$numberDecimal").GetString());
        Assert.Equal("9007199254740993", found.GetProperty("documents")[0].GetProperty("large").GetProperty("$numberLong").GetString());
        var strings = await test.Call(new(MongoOperation.Count, "orders", Object("{\"_id\":\"" + id.GetProperty("$oid").GetString() + "\"}")));
        Assert.Equal(0, strings.GetProperty("matchedCount").GetInt64());
        var updated = await test.Call(new(MongoOperation.Update, "orders", filter, Update: Object("{\"$set\":{\"customer\":\"乙\"}}")));
        Assert.Equal(1, updated.GetProperty("matchedCount").GetInt64());
        Assert.Equal(1, updated.GetProperty("modifiedCount").GetInt64());
        var noChange = await test.Call(new(MongoOperation.Update, "orders", filter, Update: Object("{\"$set\":{\"customer\":\"乙\"}}")));
        Assert.Equal(0, noChange.GetProperty("modifiedCount").GetInt64());
        var upsert = await test.Call(new(MongoOperation.Update, "orders", Object("{\"customer\":\"新\"}"), Update: Object("{\"$set\":{\"x\":1}}"), Upsert: true));
        Assert.True(upsert.TryGetProperty("upsertedId", out _));
        var collections = await test.Call(new(MongoOperation.List, NamePrefix: "ord"));
        Assert.Equal("orders", Assert.Single(collections.GetProperty("collections").EnumerateArray()).GetProperty("name").GetString());
        Assert.Equal(1, (await test.Call(new(MongoOperation.Delete, "orders", filter))).GetProperty("deletedCount").GetInt64());
        Assert.Equal(0, (await test.Call(new(MongoOperation.Delete, "orders", filter))).GetProperty("deletedCount").GetInt64());
        var tools = test.Fixture.Host.Services.GetRequiredService<ConnectionTools>();
        Assert.True(JsonDocument.Parse(await tools.TestConnection()).RootElement.GetProperty("connected").GetBoolean());
        Assert.True(JsonDocument.Parse(await tools.TestConnectionByName("primary")).RootElement.GetProperty("connected").GetBoolean());
        Assert.True(JsonDocument.Parse(await tools.TestConnectionWithRetry(0, 1)).RootElement.GetProperty("connected").GetBoolean());
        Assert.True(JsonDocument.Parse(await tools.HealthCheck()).RootElement.GetProperty("overallHealth").GetBoolean());
        var handler = new CliConfigCommandHandler();
        Assert.True(JsonDocument.Parse(await handler.TestAsync(test.Fixture.Path, "primary")).RootElement.GetProperty("connected").GetBoolean());
        Assert.True(JsonDocument.Parse(await handler.DoctorAsync(test.Fixture.Path, "primary", true, false, false)).RootElement.GetProperty("success").GetBoolean());
    }

    [MongoIntegrationFact]
    public async Task OneAndMany_ReturnActualCountsAndProtectEmptyFilters()
    {
        using var test = new MongoFixture();
        await test.Collection.InsertManyAsync(Enumerable.Range(0, 3).Select(i => new BsonDocument { { "_id", i }, { "group", 1 }, { "n", 0 } }));
        var filter = Object("{\"group\":1}");
        var update = Object("{\"$inc\":{\"n\":1}}");
        Assert.Equal(1, (await test.Call(new(MongoOperation.Update, "docs", filter, Update: update))).GetProperty("modifiedCount").GetInt64());
        Assert.Equal(3, (await test.Call(new(MongoOperation.Update, "docs", filter, Update: update, Many: true))).GetProperty("modifiedCount").GetInt64());
        Assert.Equal(1, (await test.Call(new(MongoOperation.Delete, "docs", filter))).GetProperty("deletedCount").GetInt64());
        var rejected = await test.Call(new(MongoOperation.Delete, "docs", Object("{}"), Many: true), false);
        Assert.Equal(1010, rejected.GetProperty("errorCode").GetInt32());
        Assert.Equal(2, (await test.Call(new(MongoOperation.Delete, "docs", Object("{}"), Many: true, AllowAll: true))).GetProperty("deletedCount").GetInt64());
        Assert.Equal(0, await test.Collection.CountDocumentsAsync(new BsonDocument()));
    }

    [MongoIntegrationFact]
    public async Task Find_EnforcesRowsBytesProjectionAndMultiBatchReads()
    {
        using var test = new MongoFixture();
        await test.Collection.InsertManyAsync(Enumerable.Range(0, 140).Select(i => new BsonDocument { { "_id", i }, { "group", 1 }, { "payload", new string('汉', 1000) } }));
        var bounded = await test.Call(new(MongoOperation.Find, "docs", MaxRows: 3));
        Assert.Equal(3, bounded.GetProperty("returnedCount").GetInt32());
        Assert.True(bounded.GetProperty("limitReached").GetBoolean());
        Assert.Equal(JsonValueKind.Null, bounded.GetProperty("truncated").ValueKind);
        var commands = new System.Collections.Concurrent.ConcurrentQueue<string>();
        test.OnStarted = e => commands.Enqueue(e.CommandName);
        var bytes = await test.Call(new(MongoOperation.Find, "docs", MaxResponseBytes: 1024));
        Assert.Equal(0, bytes.GetProperty("returnedCount").GetInt32());
        Assert.True(bytes.GetProperty("truncated").GetBoolean());
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(bytes.GetRawText()) <= 1024);
        Assert.Contains("killCursors", commands);
        Assert.DoesNotContain("getMore", commands);
        commands.Clear();
        var partialBytes = await test.Call(new(MongoOperation.Find, "docs", MaxResponseBytes: 7000));
        Assert.Equal(2, partialBytes.GetProperty("returnedCount").GetInt32());
        Assert.True(partialBytes.GetProperty("truncated").GetBoolean());
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(partialBytes.GetRawText()) <= 7000);
        Assert.Contains("killCursors", commands);
        var multi = await test.Call(new(MongoOperation.Find, "docs", Projection: Object("{\"payload\":0}"), Sort: Object("{\"group\":1}"), Skip: 1, MaxRows: 139));
        Assert.Equal(139, multi.GetProperty("returnedCount").GetInt32());
        Assert.Equal("1", multi.GetProperty("documents")[0].GetProperty("_id").GetProperty("$numberInt").GetString());
        Assert.DoesNotContain("payload", multi.GetProperty("documents")[0].GetRawText());
        var exact = await test.Call(new(MongoOperation.Find, "docs", Projection: Object("{\"payload\":0}"), MaxRows: 140));
        Assert.Equal(JsonValueKind.Null, exact.GetProperty("truncated").ValueKind);
        var exhausted = await test.Call(new(MongoOperation.Find, "docs", Projection: Object("{\"payload\":0}"), MaxRows: 141));
        Assert.False(exhausted.GetProperty("limitReached").GetBoolean());
        Assert.False(exhausted.GetProperty("truncated").GetBoolean());
    }

    [MongoIntegrationFact]
    public async Task CollectionDiscovery_IsBoundedAndPrefixIsLiteral()
    {
        using var test = new MongoFixture();
        foreach (var name in new[] { "a.one", "a.two", "aXthree" })
            await test.Call(new(MongoOperation.Insert, name, Document: Object("{}")));
        var matching = await test.Call(new(MongoOperation.List, NamePrefix: "a."));
        Assert.Equal(new[] { "a.one", "a.two" }, matching.GetProperty("collections").EnumerateArray().Select(c => c.GetProperty("name").GetString()).Order());
        var one = await test.Call(new(MongoOperation.List, NamePrefix: "a.", MaxRows: 1));
        Assert.Equal(1, one.GetProperty("returnedCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, one.GetProperty("truncated").ValueKind);
    }

    [MongoIntegrationFact]
    public async Task LargeUpsertConfirmation_ReportsAcknowledgedWriteWhenResponseDoesNotFit()
    {
        using var test = new MongoFixture();
        var id = new string('x', 2000);
        var filter = Object(JsonSerializer.Serialize(new { _id = id }));
        var result = await test.Call(new(MongoOperation.Update, "docs", filter, Update: Object("{\"$set\":{\"created\":true}}"),
            Upsert: true, MaxResponseBytes: 1024), false);
        Assert.Equal(1013, result.GetProperty("errorCode").GetInt32());
        Assert.Equal("acknowledged", result.GetProperty("writeOutcome").GetString());
        Assert.True(result.GetProperty("acknowledged").GetBoolean());
        Assert.Equal(1, await test.Collection.CountDocumentsAsync(new BsonDocument("_id", id)));
    }

    [MongoIntegrationFact]
    public async Task DuplicateKey_DoesNotExposeDocumentOrUriAndFailureDoesNotClaimRollback()
    {
        using var test = new MongoFixture();
        var document = Object("{\"_id\":\"private-value-123\",\"password\":\"secret-value-456\"}");
        await test.Call(new(MongoOperation.Insert, "docs", Document: document));
        var failure = await test.Call(new(MongoOperation.Insert, "docs", Document: document), false);
        Assert.Equal(1002, failure.GetProperty("errorCode").GetInt32());
        Assert.Equal("unknown", failure.GetProperty("writeOutcome").GetString());
        Assert.DoesNotContain("private-value", failure.GetRawText());
        Assert.DoesNotContain("secret-value", failure.GetRawText());
        Assert.DoesNotContain("mongodb://", failure.GetRawText());
        Assert.Equal(1, await test.Collection.CountDocumentsAsync(new BsonDocument()));
    }

    [MongoIntegrationFact]
    public async Task DeadlineAndCancellation_AreDistinctAndNextQueryWorks()
    {
        using var test = new MongoFixture();
        await test.Collection.InsertOneAsync(new BsonDocument("x", 1));
        await test.FailCommand("find", block: true);
        var timeout = await test.Call(new(MongoOperation.Find, "docs", CommandTimeoutSeconds: 1), false);
        Assert.Equal(1011, timeout.GetProperty("errorCode").GetInt32());
        await test.FailCommand("insert", block: true);
        var write = await test.Call(new(MongoOperation.Insert, "docs", Document: Object("{\"x\":2}"), CommandTimeoutSeconds: 1), false);
        Assert.Equal("unknown", write.GetProperty("writeOutcome").GetString());
        await test.FailCommand("find", block: true);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        test.OnStarted = e => { if (e.CommandName == "find") started.TrySetResult(); };
        using var cancellation = new CancellationTokenSource();
        var pending = test.Service.ExecuteAsync(new(MongoOperation.Find, "docs"), cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await test.DisableFailPoint();
        Assert.True((await test.Call(new(MongoOperation.Count, "docs"))).GetProperty("matchedCount").GetInt64() >= 1);
    }

    [MongoIntegrationFact]
    public async Task GetMoreFailure_ReleasesCursorAndAdmission()
    {
        using var test = new MongoFixture();
        await test.Collection.InsertManyAsync(Enumerable.Range(0, 130).Select(i => new BsonDocument("_id", i)));
        await test.FailCommand("getMore", block: false);
        var failed = await test.Call(new(MongoOperation.Find, "docs", MaxRows: 130), false);
        Assert.Equal(1002, failed.GetProperty("errorCode").GetInt32());
        await test.DisableFailPoint();
        Assert.Equal(130, (await test.Call(new(MongoOperation.Find, "docs", MaxRows: 131))).GetProperty("returnedCount").GetInt32());
    }

    [MongoIntegrationFact]
    public async Task MongoTools_ExecuteThroughStdioCliAndWeb()
    {
        using var test = new MongoFixture();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var transport = new StdioClientTransport(new()
        {
            Command = "dotnet", Arguments = [typeof(MongoCollectionService).Assembly.Location],
            EnvironmentVariables = new Dictionary<string, string?> { ["DB_CONFIG_PATH"] = test.Fixture.Path,
                ["ENABLE_MONITOR_CONFIG"] = "false", ["SEQ_SERVER_URL"] = "", ["SEQ_API_KEY"] = "" }
        });
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: deadline.Token);
        Assert.Equal(62, (await client.ListToolsAsync(cancellationToken: deadline.Token)).Count);
        var inserted = Payload(await client.CallToolAsync("mongo_insert_one", new Dictionary<string, object?>
        { ["collection"] = "docs", ["document"] = new { _id = "protocol", n = 1 } }, cancellationToken: deadline.Token));
        Assert.Equal("protocol", inserted.GetProperty("insertedId").GetString());
        var query = Payload(await client.CallToolAsync("mongo_find", new Dictionary<string, object?>
        { ["collection"] = "docs", ["filter"] = new { _id = "protocol" } }, cancellationToken: deadline.Token));
        Assert.Equal(1, query.GetProperty("returnedCount").GetInt32());

        var cli = new CliRunner(new CliToolCatalog(), new CliConfigCommandHandler(), new CliWebHost(), test.Fixture.StatePath, false);
        using var cliOutput = new StringWriter();
        using var cliError = new StringWriter();
        var args = new[] { "tool", "mongo_insert_one", "--collection", "docs", "--document", "{\"_id\":\"cli\"}", "--config", test.Fixture.Path };
        Assert.Equal(2, await cli.RunAsync(args, cliOutput, cliError, deadline.Token));
        Assert.Contains("--yes", cliError.ToString());
        Assert.Equal(0, await cli.RunAsync([.. args, "--yes"], cliOutput, cliError, deadline.Token));
        Assert.Equal("cli", JsonDocument.Parse(cliOutput.ToString()).RootElement.GetProperty("insertedId").GetString());

        var web = test.Fixture.Host.Services.GetRequiredService<CliWebToolService>();
        var webResult = JsonSerializer.SerializeToElement(await web.InvokeAsync("mongo_update", new(new Dictionary<string, JsonElement>
        { ["collection"] = JsonSerializer.SerializeToElement("docs"), ["filter"] = JsonSerializer.Deserialize<JsonElement>("{\"_id\":\"cli\"}"),
            ["update"] = JsonSerializer.Deserialize<JsonElement>("{\"$set\":{\"changed\":true}}") }, "mongo_update"), deadline.Token));
        Assert.True(webResult.GetProperty("toolSuccess").GetBoolean());
        Assert.Equal(1, webResult.GetProperty("result").GetProperty("modifiedCount").GetInt64());
        Assert.True((await test.Collection.Find(new BsonDocument("_id", "cli")).SingleAsync()).GetValue("changed").AsBoolean);
    }

    [MongoIntegrationFact]
    public async Task AuthenticationFailures_AreSanitizedInQueriesAndDiagnostics()
    {
        var builder = new MongoUrlBuilder(Environment.GetEnvironmentVariable("DBMCP_TEST_MONGODB"))
        { Password = "private-invalid-password" };
        using var fixture = MongoCollectionServiceTests.Fixture(uri: builder.ToString());
        var failure = Payload(await fixture.Host.Services.GetRequiredService<MongoCollectionService>()
            .ExecuteAsync(new(MongoOperation.Find, "docs", CommandTimeoutSeconds: 10), default));
        Assert.Equal(1001, failure.GetProperty("errorCode").GetInt32());
        Assert.DoesNotContain("private-invalid-password", failure.GetRawText());
        var ping = await fixture.Host.Services.GetRequiredService<ConnectionTools>().TestConnection();
        Assert.False(JsonDocument.Parse(ping).RootElement.GetProperty("success").GetBoolean());
        Assert.DoesNotContain("private-invalid-password", ping);
    }

    private sealed class MongoFixture : IDisposable
    {
        public RuntimeConsistencyTests.Fixture Fixture { get; }
        private readonly MongoClient _observer;
        private readonly MongoClientRegistry _registry;
        private readonly string _databaseName = "dbmcp_" + Guid.NewGuid().ToString("N");
        private readonly string _appName = "dbmcp-" + Guid.NewGuid().ToString("N");
        public IMongoCollection<BsonDocument> Collection => _observer.GetDatabase(_databaseName).GetCollection<BsonDocument>("docs");
        public MongoCollectionService Service => Fixture.Host.Services.GetRequiredService<MongoCollectionService>();
        public Action<CommandStartedEvent>? OnStarted { get; set; }
        public MongoFixture()
        {
            var uri = Environment.GetEnvironmentVariable("DBMCP_TEST_MONGODB") ?? throw new InvalidOperationException("Missing DBMCP_TEST_MONGODB");
            var builder = new MongoUrlBuilder(uri) { DatabaseName = _databaseName, RetryWrites = false, RetryReads = false, ApplicationName = _appName };
            _observer = new MongoClient(uri);
            _registry = new MongoClientRegistry((connection, token) =>
            {
                var settings = MongoClientSettings.FromConnectionString(connection);
                settings.ClusterConfigurator = cluster => cluster.Subscribe<CommandStartedEvent>(e => OnStarted?.Invoke(e));
                return Task.FromResult<IMongoClient>(new MongoClient(settings));
            });
            Fixture = MongoCollectionServiceTests.Fixture(dangerous: true, uri: builder.ToString(), configure: services => services.AddSingleton(_registry));
        }
        public async Task<JsonElement> Call(MongoCollectionRequest request, bool success = true)
        {
            var result = Payload(await Service.ExecuteAsync(request, default));
            Assert.Equal(success, result.GetProperty("success").GetBoolean());
            return result;
        }
        public async Task FailCommand(string command, bool block)
        {
            var data = new BsonDocument { { "failCommands", new BsonArray { command } }, { "appName", _appName } };
            if (block) { data.Add("blockConnection", true); data.Add("blockTimeMS", 4000); }
            else data.Add("errorCode", 2);
            await _observer.GetDatabase("admin").RunCommandAsync<BsonDocument>(new BsonDocument
            { { "configureFailPoint", "failCommand" }, { "mode", new BsonDocument("times", 1) }, { "data", data } });
        }
        public Task<BsonDocument> DisableFailPoint() => _observer.GetDatabase("admin").RunCommandAsync<BsonDocument>(new BsonDocument
        { { "configureFailPoint", "failCommand" }, { "mode", "off" } });
        public void Dispose()
        {
            try { _observer.GetDatabase("admin").RunCommand<BsonDocument>(new BsonDocument { { "configureFailPoint", "failCommand" }, { "mode", "off" } }); }
            finally
            {
                Fixture.Dispose();
                _registry.Dispose();
                try { _observer.DropDatabase(_databaseName); }
                finally { _observer.Dispose(); }
            }
        }
    }
}
