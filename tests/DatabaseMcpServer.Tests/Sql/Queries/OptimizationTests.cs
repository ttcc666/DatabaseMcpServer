using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using DatabaseMcpServer.Cli;
using DatabaseMcpServer.Configuration;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using DatabaseMcpServer.Tools;
using DatabaseMcpServer.Tools.Command;
using DatabaseMcpServer.Tools.Management;
using DatabaseMcpServer.Tools.Query;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseMcpServer.Tests;

public class OptimizationTests
{
    [Fact]
    public async Task FullCatalog_FiltersProviderAndAccessAndRejectsHiddenCliCalls()
    {
        using var fixture = DatabaseRoleTests.Create(new DatabaseExecutionOptions { Access = DatabaseToolAccess.Read });
        var catalog = new ToolCatalog();
        using var policy = new ToolExposurePolicy();
        var selected = policy.Select(catalog, fixture.Service, new() { Access = DatabaseToolAccess.Read });
        Assert.Contains("query_database", selected);
        Assert.DoesNotContain("mongo_find", selected);
        Assert.DoesNotContain("create_table", selected);
        var config = new ConfigurationFileStore().Load(fixture.Path);
        config.Databases = [new() { Name = "mongo", DbType = "MongoDb", ConnectionString = "mongodb://localhost:1/test", IsDefault = true }];
        File.WriteAllText(fixture.Path, JsonSerializer.Serialize(config));
        Assert.True(fixture.Service.ReloadConfiguration().Success);
        Assert.Equal(ToolContract.MongoFull, policy.Select(catalog, fixture.Service, new()).Order());
        using var output = new StringWriter(); using var error = new StringWriter();
        Assert.Equal(0, await new CliRunner().RunAsync(["tool", "list", "--json", "--config", fixture.Path], output, error));
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal(ToolContract.MongoFull, json.RootElement.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()));
        output.GetStringBuilder().Clear(); error.GetStringBuilder().Clear();
        Assert.Equal(2, await new CliRunner().RunAsync(["tool", "query_database", "--sql", "select 1", "--config", fixture.Path], output, error));
        Assert.Contains("未在当前配置", error.ToString());
    }

    [Fact]
    public async Task ReaderPage_StopsAtByteBudgetAndReleasesConnection()
    {
        using var fixture = DatabaseRoleTests.Create(new DatabaseExecutionOptions(MaxConcurrent: 1));
        using (var db = fixture.Sessions.AcquireClient()) db.Client.Ado.ExecuteCommand("CREATE TABLE wide_row(id integer PRIMARY KEY, value text); INSERT INTO wide_row VALUES(1,hex(zeroblob(10000)))");
        DatabaseRoleTests.Error(await fixture.Host.Services.GetRequiredService<QueryTools>().QueryTablePage("wide_row", maxResponseBytes: 2048), 1013);
        var next = DatabaseRoleTests.Success(await fixture.Host.Services.GetRequiredService<DatabaseQueryTools>().QueryDatabase("select 7 as value"));
        Assert.Equal(7, next.GetProperty("data")[0].GetProperty("value").GetInt32());
    }

    [Fact]
    public async Task CompositeCursor_ContinuesAfterDeletedRowsAndRejectsChangedBinding()
    {
        using var fixture = DatabaseRoleTests.Create();
        using (var db = fixture.Sessions.AcquireClient())
            db.Client.Ado.ExecuteCommand("CREATE TABLE composite_key(a int NOT NULL,b text NOT NULL,PRIMARY KEY(b,a)); INSERT INTO composite_key VALUES(1,'a'),(2,'a'),(1,'b'),(2,'b')");
        var tool = fixture.Host.Services.GetRequiredService<QueryTools>();
        var first = DatabaseRoleTests.Success(await tool.QueryTablePage("composite_key", pageSize: 2));
        Assert.Equal(new[] { "b", "a" }, first.GetProperty("orderBy").EnumerateArray().Select(v => v.GetString()));
        var cursor = first.GetProperty("nextCursor").GetString();
        Assert.NotNull(cursor);
        using (var db = fixture.Sessions.AcquireClient())
            db.Client.Ado.ExecuteCommand("DELETE FROM composite_key WHERE b='a'; INSERT INTO composite_key VALUES(0,'a')");
        var next = DatabaseRoleTests.Success(await tool.QueryTablePage("composite_key", pageSize: 2, cursor: cursor));
        Assert.Equal(new[] { 1, 2 }, next.GetProperty("data").EnumerateArray().Select(r => r.GetProperty("a").GetInt32()));
        Assert.All(next.GetProperty("data").EnumerateArray(), row => Assert.Equal("b", row.GetProperty("b").GetString()));
        DatabaseRoleTests.Error(await tool.QueryTablePage("composite_key", pageSize: 3, cursor: cursor), 1004);
        DatabaseRoleTests.Error(await tool.QueryTablePage("composite_key", page: 2, pageSize: 2, cursor: cursor), 1004);
        DatabaseRoleTests.Error(await tool.QueryTablePage("composite_key", databaseName: "analytics", pageSize: 2, cursor: cursor), 1004);
        File.WriteAllText(fixture.Path, File.ReadAllText(fixture.Path).Replace("Pooling=False", "Pooling=False;Default Timeout=20", StringComparison.Ordinal));
        Assert.True(fixture.Service.ReloadConfiguration().Success);
        DatabaseRoleTests.Error(await tool.QueryTablePage("composite_key", pageSize: 2, cursor: cursor), 1004);
    }

    [Fact]
    public async Task ObjectCursor_DoesNotSkipAfterAnEarlierObjectIsDeleted()
    {
        using var fixture = DatabaseRoleTests.Create();
        using (var db = fixture.Sessions.AcquireClient()) db.Client.Ado.ExecuteCommand("CREATE TABLE seek_a(id int); CREATE TABLE seek_b(id int); CREATE TABLE seek_c(id int)");
        var tool = fixture.Host.Services.GetRequiredService<ObjectMetadataTools>();
        var first = DatabaseRoleTests.Success(await tool.ListObjects("table", namePrefix: "seek_", limit: 1));
        using (var db = fixture.Sessions.AcquireClient()) db.Client.Ado.ExecuteCommand("DROP TABLE seek_a");
        var second = DatabaseRoleTests.Success(await tool.ListObjects("table", namePrefix: "seek_", limit: 1, cursor: first.GetProperty("nextCursor").GetString()));
        Assert.Equal("seek_b", second.GetProperty("objects")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task SchemaBatch_ReportsMissingTablesAndHonorsSharedBudgetOnCacheHits()
    {
        using var fixture = DatabaseRoleTests.Create();
        using (var db = fixture.Sessions.AcquireClient()) db.Client.Ado.ExecuteCommand("CREATE TABLE meta_second(a int,b int)");
        var tool = fixture.Host.Services.GetRequiredService<SchemaTools>();
        DatabaseRoleTests.Success(await tool.GetTableSchema("meta_second", sections: ["columns"]));
        var result = DatabaseRoleTests.Success(await tool.GetTableSchema(tableNames: ["markers", "missing", "meta_second"], sections: ["columns"], maxRows: 2));
        var tables = result.GetProperty("tables");
        Assert.Equal("available", tables[0].GetProperty("status").GetString());
        Assert.Equal("unavailable", tables[1].GetProperty("status").GetString());
        Assert.Equal(1002, tables[1].GetProperty("errorCode").GetInt32());
        Assert.Equal(1013, tables[2].GetProperty("errorCode").GetInt32());
        DatabaseRoleTests.Error(await tool.GetTableSchema("markers", tableNames: ["meta_second"]), 1004);
        DatabaseRoleTests.Error(await tool.GetTableSchema(tableNames: []), 1004);
        DatabaseRoleTests.Error(await tool.GetTableSchema(tableNames: ["markers", "markers"]), 1004);
        var exhausted = DatabaseRoleTests.Success(await tool.GetTableSchema(tableNames: ["meta_second", "markers"], sections: ["columns"], maxRows: 1));
        Assert.All(exhausted.GetProperty("tables").EnumerateArray(), table => Assert.Equal(1013, table.GetProperty("errorCode").GetInt32()));
    }

    [Fact]
    public async Task MetadataDeadline_IncludesAdmissionAndRecordsCancellationCompletion()
    {
        using var fixture = DatabaseRoleTests.Create(new DatabaseExecutionOptions(MaxConcurrent: 1));
        var metrics = new List<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meter) => { if (instrument.Meter.Name == DatabaseTelemetry.MeterName) meter.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => { Assert.Empty(tags.ToArray()); lock (metrics) metrics.Add(instrument.Name); });
        listener.Start();
        using (var held = fixture.Sessions.AcquireClient())
        {
            var watch = Stopwatch.StartNew();
            DatabaseRoleTests.Error(await fixture.Host.Services.GetRequiredService<SchemaTools>().GetTableSchema("markers", commandTimeoutSeconds: 1), 1011);
            Assert.InRange(watch.Elapsed.TotalSeconds, 0.8, 5);
        }
        DatabaseRoleTests.Success(await fixture.Host.Services.GetRequiredService<SchemaTools>().GetTableSchema("markers"));
        Assert.Contains("dbmcp.cancellation.completion", metrics);
    }

    [Fact]
    public async Task AsyncCache_CancelledWaiterDoesNotCancelOwnerOrPublishFailedLoad()
    {
        using var fixture = DatabaseRoleTests.Create();
        using var lease = fixture.Sessions.AcquireClient();
        var cache = new DatabaseMetadataCache(new());
        var release = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var loads = 0;
        Task<JsonElement> Load() { loads++; return release.Task; }
        var owner = cache.GetAsync(lease.Context, "t", false, Load, default);
        using var cancel = new CancellationTokenSource();
        var waiter = cache.GetAsync(lease.Context, "t", false, Load, cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
        Assert.False(owner.IsCompleted);
        release.SetResult(JsonSerializer.SerializeToElement(42));
        Assert.Equal(42, (await owner).GetInt32());
        Assert.Equal(42, (await cache.GetAsync(lease.Context, "t", false, Load, default)).GetInt32());
        Assert.Equal(1, loads);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.GetAsync(lease.Context, "bad", false,
            () => Task.FromCanceled<JsonElement>(new CancellationToken(true)), new CancellationToken(true)));
        Assert.Equal(42, (await cache.GetAsync(lease.Context, "bad", false, Load, default)).GetInt32());
        using var ownerCancellation = new CancellationTokenSource();
        async Task<JsonElement> CancelledLoad()
        {
            await Task.Delay(Timeout.Infinite, ownerCancellation.Token);
            return default;
        }
        var cancelledOwner = cache.GetAsync(lease.Context, "owner-cancel", false, CancelledLoad, ownerCancellation.Token);
        var recoveringWaiter = cache.GetAsync(lease.Context, "owner-cancel", false, Load, default);
        ownerCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledOwner);
        Assert.Equal(42, (await recoveringWaiter.WaitAsync(TimeSpan.FromSeconds(5))).GetInt32());
        var oversizedLoads = 0;
        Task<JsonElement> OversizedLoad()
        {
            oversizedLoads++;
            return Task.FromResult(JsonSerializer.SerializeToElement(new string('x', 1_048_576)));
        }
        await cache.GetAsync(lease.Context, "oversized", false, OversizedLoad, default);
        await cache.GetAsync(lease.Context, "oversized", false, OversizedLoad, default);
        Assert.Equal(2, oversizedLoads);
    }

    [Fact]
    public async Task UnsupportedProcedure_ReportsActionableCapabilityError()
    {
        using var fixture = DatabaseRoleTests.Create();
        using var error = JsonDocument.Parse(await fixture.Host.Services.GetRequiredService<CommandTools>().CallStoredProcedure("unused"));
        Assert.Equal(1012, error.RootElement.GetProperty("errorCode").GetInt32());
        Assert.False(error.RootElement.GetProperty("retryable").GetBoolean());
        Assert.Equal("capability", error.RootElement.GetProperty("phase").GetString());
        Assert.Equal("StoredProcedures", error.RootElement.GetProperty("capability").GetString());
    }

    [Fact]
    public void TypedContracts_RejectOldNamesAndInvalidColumnFields()
    {
        var catalog = new ToolCatalog();
        var batch = catalog.Tools.Single(t => t.Name == "batch_sql_query");
        Assert.DoesNotContain(batch.Parameters, p => p.ParameterName == "queries");
        Assert.Throws<InvalidOperationException>(() => ToolInvoker.BindArguments(batch, new Dictionary<string, string?> { ["queries"] = "[\"select 1\"]" }));
        var create = catalog.Tools.Single(t => t.Name == "create_table");
        Assert.Throws<InvalidOperationException>(() => ToolInvoker.BindArguments(create, new Dictionary<string, string?>
            { ["table-name"] = "t", ["columns"] = """[{"DbColumnName":"id","DataType":"int"}]""" }));
        Assert.Throws<DatabaseMcpException>(() => SchemaColumnDefinitionParser.Parse(new() { Name = "amount", DataType = "decimal", Precision = 3, Scale = 4 }));
        Assert.Throws<DatabaseMcpException>(() => SchemaColumnDefinitionParser.ParseMany([new() { Name = "a", DataType = "int" }, new() { Name = "A", DataType = "int" }]));
    }

    [Theory]
    [InlineData("csv")]
    [InlineData("jsonl")]
    public async Task Export_PublishesRowsAndPreservesExistingFileOnErrors(string format)
    {
        using var fixture = DatabaseRoleTests.Create();
        var path = Path.Combine(fixture.Directory.FullName, "output." + format);
        async Task<(int Exit, string Output)> Run(string sql, params string[] extra)
        {
            using var output = new StringWriter(); using var error = new StringWriter();
            var result = await new CliRunner().RunAsync(["export", "--sql", sql, "--output", path, "--format", format, "--config", fixture.Path, ..extra], output, error);
            return (result, output.ToString());
        }
        var success = await Run("SELECT 7 AS id, '中文,\"line' AS text UNION ALL SELECT 8,NULL");
        Assert.Equal(0, success.Exit);
        using var json = JsonDocument.Parse(success.Output);
        Assert.Equal(2, json.RootElement.GetProperty("summary").GetProperty("rowCount").GetInt32());
        Assert.DoesNotContain("中文", success.Output);
        var original = File.ReadAllText(path);
        if (format == "csv") Assert.Contains("\"中文,\"\"line\"", original);
        else Assert.Equal(7, JsonDocument.Parse(original.Split('\n')[0]).RootElement.GetProperty("id").GetInt32());
        Assert.NotEqual(0, (await Run("SELECT 99 AS value")).Exit);
        Assert.NotEqual(0, (await Run("SELECT * FROM missing", "--overwrite")).Exit);
        Assert.Equal(original, File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(fixture.Directory.FullName, ".dbmcp-export-*.tmp"));
        Assert.Equal(0, (await Run("SELECT 99 AS value", "--overwrite")).Exit);
        Assert.Contains("99", File.ReadAllText(path));
    }

    [Fact]
    public async Task Export_EmptyParameterizedLimitsAndCancellationPreserveFile()
    {
        using var fixture = DatabaseRoleTests.Create();
        var path = Path.Combine(fixture.Directory.FullName, "limits.jsonl");
        async Task<(int Exit, string Output)> Run(string sql, CancellationToken token = default, params string[] extra)
        {
            using var output = new StringWriter(); using var error = new StringWriter();
            var exit = await new CliRunner().RunAsync(["export", "--sql", sql, "--output", path, "--overwrite", "--config", fixture.Path, ..extra], output, error, token);
            return (exit, output.ToString());
        }
        var empty = await Run("SELECT 1 AS value WHERE 1=0");
        Assert.Equal(0, empty.Exit);
        Assert.Empty(File.ReadAllBytes(path));
        var parameterized = await Run("SELECT @value AS value UNION ALL SELECT 'second'", default,
            "--parameters", """[{"name":"value","value":"参数"}]""", "--max-rows", "1");
        Assert.Equal(0, parameterized.Exit);
        using var summary = JsonDocument.Parse(parameterized.Output);
        Assert.True(summary.RootElement.GetProperty("summary").GetProperty("limitReached").GetBoolean());
        var original = File.ReadAllText(path);
        using var row = JsonDocument.Parse(original);
        Assert.Equal("参数", row.RootElement.GetProperty("value").GetString());
        var overBudget = await Run("WITH RECURSIVE n(id) AS (SELECT 1 UNION ALL SELECT id+1 FROM n WHERE id<2000) SELECT hex(zeroblob(512)) AS value FROM n", default, "--max-file-mib", "1");
        Assert.Equal(1, overBudget.Exit);
        using var failure = JsonDocument.Parse(overBudget.Output);
        Assert.Equal(1013, failure.RootElement.GetProperty("errorCode").GetInt32());
        Assert.Equal(original, File.ReadAllText(path));
        var cancelled = await Run("SELECT 99 AS value", new CancellationToken(true));
        Assert.Equal(130, cancelled.Exit);
        Assert.Equal(original, File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(fixture.Directory.FullName, ".dbmcp-export-*.tmp"));
    }
}
