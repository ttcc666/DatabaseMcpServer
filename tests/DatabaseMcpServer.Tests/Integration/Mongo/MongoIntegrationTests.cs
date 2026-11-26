using System.Text.Json;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using DatabaseMcpServer.Tools.Management;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
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
    public async Task ReadTools_ReturnProviderConvertedValuesAndTypedFilters()
    {
        using var test = new MongoFixture();
        const string source = """
            {"_id":{"$oid":"507f1f77bcf86cd799439011"},"optional":null,"textId":"507f1f77bcf86cd799439011",
             "long":{"$numberLong":"9007199254740993"},"decimal":{"$numberDecimal":"12.34"},
             "uuid":{"$binary":{"base64":"AAAAAAAAAAAAAAAAAAAAAA==","subType":"04"}},"nested":[null,{"ok":true}]}
            """;
        await test.Call(new(MongoOperation.InsertMany, "docs", Documents: [Object(source)]));
        foreach (var operation in new[] { MongoOperation.Find, MongoOperation.Aggregate })
        {
            var result = await test.Call(new(operation, "docs", Pipeline: operation == MongoOperation.Aggregate ? [Object("{\"$match\":{}}")]: null));
            var document = result.GetProperty("documents")[0];
            Assert.Equal("507f1f77bcf86cd799439011", document.GetProperty("_id").GetString());
            Assert.Equal(document.GetProperty("_id").GetString(), document.GetProperty("textId").GetString());
            Assert.Equal(9007199254740993L, document.GetProperty("long").GetInt64());
            Assert.Equal(12.34m, document.GetProperty("decimal").GetDecimal());
            Assert.Equal(new byte[16], document.GetProperty("uuid").GetBytesFromBase64());
            Assert.Equal(JsonValueKind.Null, document.GetProperty("nested")[0].ValueKind);
            Assert.True(document.GetProperty("nested")[1].GetProperty("ok").GetBoolean());
            Assert.False(document.TryGetProperty("absent", out _));
            Assert.Equal(JsonValueKind.Null, document.GetProperty("optional").ValueKind);
            var filter = new Dictionary<string, JsonElement> { ["_id"] = document.GetProperty("_id").Clone() };
            Assert.Equal(0, (await test.Call(new(MongoOperation.Find, "docs", filter))).GetProperty("returnedCount").GetInt32());
            Assert.Equal(1, (await test.Call(new(MongoOperation.Find, "docs", Object("""{"_id":{"$oid":"507f1f77bcf86cd799439011"}}""")))).GetProperty("returnedCount").GetInt32());
        }
    }

    [MongoIntegrationFact]
    public async Task ProviderConversion_NormalizesMissingFieldsAndReportsDecimalOverflow()
    {
        using var test = new MongoFixture();
        await test.Collection.InsertManyAsync([new BsonDocument { { "_id", 1 }, { "optional", BsonNull.Value } }, new BsonDocument("_id", 2)]);
        var result = await test.Call(new(MongoOperation.Find, "docs"));
        Assert.Equal(2, result.GetProperty("returnedCount").GetInt32());
        Assert.All(result.GetProperty("documents").EnumerateArray(), row => Assert.Equal(JsonValueKind.Null, row.GetProperty("optional").ValueKind));
        await test.Collection.InsertOneAsync(new BsonDocument { { "_id", 3 }, { "decimal", Decimal128.Parse("1E+100") } });
        var failure = await test.Call(new(MongoOperation.Find, "docs"), false);
        Assert.Equal((int)DatabaseErrorCode.QueryExecutionFailed, failure.GetProperty("errorCode").GetInt32());
        Assert.DoesNotContain("1E+100", failure.GetRawText());
        Assert.Equal(3, (await test.Call(new(MongoOperation.Find, "docs", Projection: Object("{\"_id\":1}")))).GetProperty("returnedCount").GetInt32());
    }

    [MongoIntegrationFact]
    public async Task ReadBudgets_ApplyAfterProviderMaterialization()
    {
        using var test = new MongoFixture();
        await test.Collection.InsertManyAsync(Enumerable.Range(1, 110).Select(i =>
            new BsonDocument { { "_id", i }, { "payload", new string('x', 4000) } }));
        await test.FailCommand("getMore", block: false);
        var failure = await test.Call(new(MongoOperation.Aggregate, "docs", Pipeline: [Object("{\"$sort\":{\"_id\":1}}")],
            MaxRows: 110, MaxResponseBytes: 1024), false);
        Assert.Equal((int)DatabaseErrorCode.QueryExecutionFailed, failure.GetProperty("errorCode").GetInt32());
        await test.DisableFailPoint();
        foreach (var byteLimited in new[] { false, true })
        {
            var result = await test.Call(new(MongoOperation.Aggregate, "docs",
                Pipeline: [Object("{\"$sort\":{\"_id\":1}}")], MaxRows: byteLimited ? 20 : 1,
                MaxResponseBytes: byteLimited ? 1024 : 10000));
            Assert.Equal(byteLimited ? 0 : 1, result.GetProperty("returnedCount").GetInt32());
            Assert.True(result.GetProperty("limitReached").GetBoolean());
            Assert.True(System.Text.Encoding.UTF8.GetByteCount(result.GetRawText()) <= (byteLimited ? 1024 : 10000));
            if (byteLimited) Assert.True(result.GetProperty("truncated").GetBoolean());
            else Assert.Equal(JsonValueKind.Null, result.GetProperty("truncated").ValueKind);
        }
    }

    [MongoIntegrationFact]
    public async Task Aggregation_HandlesEmptyResultsAndRowCeilingWithoutUserLimit()
    {
        using var test = new MongoFixture();
        var empty = await test.Call(new(MongoOperation.Aggregate, "docs", Pipeline: [Object("{\"$match\":{}}")], MaxRows: 2));
        Assert.Empty(empty.GetProperty("documents").EnumerateArray());
        Assert.False(empty.GetProperty("limitReached").GetBoolean());
        await test.Collection.InsertManyAsync(Enumerable.Range(1, 5).Select(i => new BsonDocument("_id", i)));
        var result = await test.Call(new(MongoOperation.Aggregate, "docs", Pipeline: [Object("{\"$sort\":{\"_id\":1}}")], MaxRows: 2));
        Assert.Equal(new[] { 1, 2 }, result.GetProperty("documents").EnumerateArray()
            .Select(doc => BsonDocument.Parse(doc.GetRawText())["_id"].AsInt32));
        Assert.True(result.GetProperty("limitReached").GetBoolean());
    }

    [MongoIntegrationFact]
    public async Task Cancellation_WaitsForInitialCommandAndReleasesAdmission()
    {
        foreach (var command in new[] { "aggregate", "getMore" })
        {
            using var test = new MongoFixture(configure: services => services.AddSingleton(new DatabaseExecutionOptions { MaxTotalConcurrent = 1 }));
            await test.Collection.InsertManyAsync(Enumerable.Range(1, 110).Select(i => new BsonDocument("_id", i)));
            var count = await test.FailCommand(command, block: true);
            using var cancellation = new CancellationTokenSource();
            var pending = Task.Run(() => test.Service.ExecuteAsync(new(MongoOperation.Find, "docs"), cancellation.Token));
            await test.WaitForFailPoint(count + 1);
            cancellation.Cancel();
            if (command == "aggregate")
            {
                await Task.Delay(100);
                Assert.False(pending.IsCompleted);
                var queued = test.Fixture.Sessions.AcquireClientAsync("analytics").AsTask();
                Assert.False(queued.IsCompleted);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(12)));
                using var lease = await queued.WaitAsync(TimeSpan.FromSeconds(3));
            }
            else
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(3)));
            await test.DisableFailPoint();
            Assert.Equal(110, (await test.Call(new(MongoOperation.Find, "docs"))).GetProperty("returnedCount").GetInt32());
        }
    }

    [MongoIntegrationFact]
    public async Task Deadline_ReturnsTimeoutAndProbeCancellationReleasesAdmission()
    {
        using var test = new MongoFixture(configure: services => services.AddSingleton(new DatabaseExecutionOptions { MaxTotalConcurrent = 1 }));
        await test.Collection.InsertOneAsync(new BsonDocument("_id", 1));
        await test.FailCommand("aggregate", block: true);
        var timeout = await Task.Run(() => test.Call(new(MongoOperation.Find, "docs", CommandTimeoutSeconds: 1), false)).WaitAsync(TimeSpan.FromSeconds(12));
        Assert.Equal(1011, timeout.GetProperty("errorCode").GetInt32());
        await test.DisableFailPoint();
        var count = await test.FailCommand("aggregate", block: true);
        using var cancellation = new CancellationTokenSource();
        var tools = test.Fixture.Host.Services.GetRequiredService<ConnectionTools>();
        var probe = Task.Run(() => tools.TestConnection(cancellation.Token));
        await test.WaitForFailPoint(count + 1);
        cancellation.Cancel();
        await Task.Delay(100);
        Assert.False(probe.IsCompleted);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe.WaitAsync(TimeSpan.FromSeconds(12)));
        await test.DisableFailPoint();
        Assert.Equal(1, (await test.Call(new(MongoOperation.Find, "docs"))).GetProperty("returnedCount").GetInt32());
    }

    [MongoIntegrationFact]
    public async Task GlobalAdmission_SharesSqlSlotAndRunsMongoAfterRelease()
    {
        using var test = new MongoFixture(configure: services => services.AddSingleton(new DatabaseExecutionOptions { MaxTotalConcurrent = 1 }));
        await test.Collection.InsertOneAsync(new BsonDocument("value", 7));
        using var sql = test.Fixture.Sessions.AcquireClient("analytics");
        var pending = test.Service.ExecuteAsync(new(MongoOperation.Find, "docs"), default);
        Assert.False(pending.IsCompleted);
        sql.Dispose();
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(result.Success);
        Assert.Equal(1, Payload(result).GetProperty("returnedCount").GetInt32());
    }

    [MongoIntegrationFact]
    public async Task CollectionTools_RoundTripInsertFindUpdateDeleteTruncateAndProbe()
    {
        using var test = new MongoFixture();
        var inserted = await test.Call(new(MongoOperation.InsertMany, "orders", Documents: [Object("""{"_id":"o1","customer":"甲","amount":{"$numberDecimal":"12.34"},"large":{"$numberLong":"9007199254740993"}}""")]));
        Assert.Equal(1, inserted.GetProperty("insertedCount").GetInt64());
        var found = await test.Call(new(MongoOperation.Find, "orders", Object("""{"_id":"o1"}""")));
        Assert.Equal("甲", found.GetProperty("documents")[0].GetProperty("customer").GetString());
        var updated = await test.Call(new(MongoOperation.Update, "orders", Object("""{"_id":"o1"}"""), Update: Object("""{"$set":{"customer":"乙"}}""")));
        Assert.Equal(1, updated.GetProperty("modifiedCount").GetInt64());
        Assert.Equal(1, (await test.Call(new(MongoOperation.Delete, "orders", Object("""{"_id":"o1"}""")))).GetProperty("deletedCount").GetInt64());
        await test.Call(new(MongoOperation.InsertMany, "orders", Documents: [Object("""{"customer":"丙"}""")]));
        await test.Call(new(MongoOperation.Truncate, "orders"));
        Assert.Equal(0, await test.Collection.Database.GetCollection<BsonDocument>("orders").CountDocumentsAsync(new BsonDocument()));
        var tools = test.Fixture.Host.Services.GetRequiredService<ConnectionTools>();
        Assert.True(TestJson.Parse(await tools.TestConnection()).RootElement.GetProperty("connected").GetBoolean());
        Assert.True(TestJson.Parse(await tools.HealthCheck()).RootElement.GetProperty("overallHealth").GetBoolean());
    }

    private sealed class MongoFixture : IDisposable
    {
        public RuntimeConsistencyTests.Fixture Fixture { get; }
        private readonly MongoClient _observer;
        private readonly string _databaseName = "dbmcp_" + Guid.NewGuid().ToString("N");
        public IMongoCollection<BsonDocument> Collection => _observer.GetDatabase(_databaseName).GetCollection<BsonDocument>("docs");
        public MongoCollectionService Service => Fixture.Host.Services.GetRequiredService<MongoCollectionService>();
        public MongoFixture(Action<IServiceCollection>? configure = null)
        {
            var uri = Environment.GetEnvironmentVariable("DBMCP_TEST_MONGODB") ?? throw new InvalidOperationException("Missing DBMCP_TEST_MONGODB");
            var builder = new MongoUrlBuilder(uri) { DatabaseName = _databaseName, RetryWrites = false, RetryReads = false, ApplicationName = _databaseName };
            _observer = new MongoClient(uri);
            Fixture = MongoCollectionServiceTests.Fixture(dangerous: true, uri: builder.ToString(), configure: configure);
        }
        public async Task<JsonElement> Call(MongoCollectionRequest request, bool success = true)
        {
            var result = Payload(await Service.ExecuteAsync(request, default));
            Assert.Equal(success, result.GetProperty("success").GetBoolean());
            return result;
        }
        public async Task<long> FailCommand(string command, bool block)
        {
            var data = new BsonDocument { { "failCommands", new BsonArray { command } }, { "appName", _databaseName } };
            if (block) { data.Add("blockConnection", true); data.Add("blockTimeMS", 8000); }
            else data.Add("errorCode", 2);
            var result = await _observer.GetDatabase("admin").RunCommandAsync<BsonDocument>(new BsonDocument
            { { "configureFailPoint", "failCommand" }, { "mode", new BsonDocument("times", 1) }, { "data", data } });
            return result["count"].ToInt64();
        }
        public Task<BsonDocument> WaitForFailPoint(long count) => _observer.GetDatabase("admin").RunCommandAsync<BsonDocument>(new BsonDocument
        { { "waitForFailPoint", "failCommand" }, { "timesEntered", count }, { "maxTimeMS", 5000 } });
        public Task<BsonDocument> DisableFailPoint() => _observer.GetDatabase("admin").RunCommandAsync<BsonDocument>(new BsonDocument
        { { "configureFailPoint", "failCommand" }, { "mode", "off" } });
        public void Dispose()
        {
            try
            {
                _observer.GetDatabase("admin").RunCommand<BsonDocument>(new BsonDocument
                { { "configureFailPoint", "failCommand" }, { "mode", "off" } });
            }
            finally
            {
                try { Fixture.Dispose(); }
                finally
                {
                    try { _observer.DropDatabase(_databaseName); }
                    finally { _observer.Dispose(); }
                }
            }
        }
    }
}
