using System.Diagnostics.Metrics;
using System.Text.Json;
using DatabaseMcpServer.Cli;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Tools.Command;
using DatabaseMcpServer.Tools.Management;
using DatabaseMcpServer.Tools.Query;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace DatabaseMcpServer.Tests;

public class DatabaseRoleTests
{
    [Fact]
    public async Task WebTools_RejectObsoleteArgumentsAndExecuteTypedBatch()
    {
        using var fixture = Create();
        var web = fixture.Host.Services.GetRequiredService<DatabaseMcpServer.Web.CliWebToolService>();
        var obsolete = new Dictionary<string, JsonElement>
        {
            ["sql"] = JsonSerializer.SerializeToElement("insert into markers values (99)"),
            ["typed-parameters"] = JsonSerializer.SerializeToElement(Array.Empty<object>())
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => web.InvokeAsync("execute_command", new(obsolete, "execute_command"), default));
        var oldBatch = new Dictionary<string, JsonElement> { ["commands"] = JsonSerializer.SerializeToElement(new[] { "insert into markers values (99)" }) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => web.InvokeAsync("batch_execute_commands", new(oldBatch, "batch_execute_commands"), default));
        var arguments = new Dictionary<string, JsonElement>
        {
            ["commands"] = JsonSerializer.Deserialize<JsonElement>("""[{"sql":"insert into markers values (@v)","parameters":[{"name":"v","value":7}]}]""")
        };
        var result = JsonSerializer.SerializeToElement(await web.InvokeAsync("batch_execute_commands", new(arguments, "batch_execute_commands"), default));
        Assert.True(result.GetProperty("toolSuccess").GetBoolean());
        Assert.Equal(1, result.GetProperty("result").GetProperty("results")[0].GetProperty("affectedRows").GetInt32());
        using var lease = fixture.Service.AcquireClient();
        Assert.Equal(new[] { 1, 7 }, lease.Client.Ado.SqlQuery<int>("select value from markers order by value"));
    }

    [Fact]
    public async Task NamedRequests_ReadWriteAndSchemaStayOnTargetWithoutChangingSelection()
    {
        using var fixture = Create();
        var before = File.ReadAllText(fixture.Path);
        var state = File.Exists(fixture.StatePath) ? File.ReadAllText(fixture.StatePath) : null;
        var queries = fixture.Host.Services.GetRequiredService<DatabaseQueryTools>();
        var commands = fixture.Host.Services.GetRequiredService<CommandTools>();
        Success(await commands.ExecuteCommand("insert into markers values (3)", databaseName: "analytics"));
        var result = await queries.QueryDatabase("select value from markers order by value", "analytics");
        var data = Structured(result);
        Assert.Equal("analytics", data.GetProperty("databaseName").GetString());
        Assert.Equal(new[] { 2, 3 }, data.GetProperty("data").EnumerateArray().Select(row => row.GetProperty("value").GetInt32()));
        Assert.True(data.GetProperty("configurationVersion").GetInt64() > 0);
        var primary = Structured(await queries.QueryDatabase("select value from markers"));
        Assert.Equal(1, Assert.Single(primary.GetProperty("data").EnumerateArray()).GetProperty("value").GetInt32());
        var schema = fixture.Host.Services.GetRequiredService<SchemaTools>();
        Success(schema.CreateTable("only_analytics", """[{"DbColumnName":"id","DataType":"int"}]""", databaseName: "analytics"));
        Assert.False(Success(schema.IsAnyTable("only_analytics", databaseName: "primary")).GetProperty("exists").GetBoolean());
        Assert.True(Success(schema.IsAnyTable("only_analytics", databaseName: "analytics")).GetProperty("exists").GetBoolean());
        Assert.Equal("primary", fixture.Service.GetCurrentDatabaseName());
        Assert.Equal(before, File.ReadAllText(fixture.Path));
        Assert.Equal(state, File.Exists(fixture.StatePath) ? File.ReadAllText(fixture.StatePath) : null);
    }

    [Fact]
    public async Task NamedQueuedRequest_KeepsVersionAndPolicyAcrossRenameAndReload()
    {
        using var fixture = Create(new(1, 2, 10));
        using var active = fixture.Service.AcquireClient("analytics");
        var version = active.Context.ConfigurationVersion;
        var waiting = fixture.Service.AcquireClientAsync("analytics").AsTask();
        Assert.False(waiting.IsCompleted);
        Assert.True(fixture.Service.RenameDatabase("analytics", "reports").Success);
        var config = new CliConfigFileService().Load(fixture.Path);
        config.Databases.Single(d => d.Name == "reports").AccessMode = "read-only";
        File.WriteAllText(fixture.Path, JsonSerializer.Serialize(config));
        Assert.True(fixture.Service.ReloadConfiguration().Success);
        active.Dispose();
        using (var queued = await waiting.WaitAsync(TimeSpan.FromSeconds(5)))
        {
            Assert.Equal("analytics", queued.Context.DatabaseName);
            Assert.Equal(version, queued.Context.ConfigurationVersion);
            Assert.Null(queued.Context.AccessMode);
            Assert.Equal(2, queued.Client.Ado.GetInt("select value from markers"));
        }
        using var next = await fixture.Service.AcquireClientAsync("reports");
        Assert.True(next.Context.ConfigurationVersion > version);
        Assert.Equal("read-only", next.Context.AccessMode);
    }

    [Theory]
    [InlineData("missing", 1005)]
    [InlineData(" ", 1004)]
    public async Task ExplicitInvalidTarget_DoesNotFallBack(string target, int code)
    {
        using var fixture = Create();
        var result = await fixture.Host.Services.GetRequiredService<DatabaseQueryTools>().QueryDatabase("select 1", target);
        Assert.True(result.IsError);
        Assert.Equal(code, result.StructuredContent!.Value.GetProperty("errorCode").GetInt32());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReadOnlyPolicy_BlocksAllMutationPathsButAllowsQueries(bool serverPolicy)
    {
        using var fixture = Create(new() { Access = serverPolicy ? DatabaseToolAccess.Read : DatabaseToolAccess.Schema },
            serverPolicy ? null : "read-only");
        var commands = fixture.Host.Services.GetRequiredService<CommandTools>();
        Error(await commands.ExecuteCommand("insert into markers values (99)", databaseName: "analytics"), 1010);
        Error(commands.CallStoredProcedure("anything", databaseName: "analytics"), 1010);
        Error(fixture.Host.Services.GetRequiredService<SchemaTools>().DropTable("markers", "analytics"), 1010);
        var query = Structured(await fixture.Host.Services.GetRequiredService<DatabaseQueryTools>().QueryDatabase("select value from markers", "analytics"));
        Assert.Equal(2, Assert.Single(query.GetProperty("data").EnumerateArray()).GetProperty("value").GetInt32());
        var catalog = new CliToolCatalog();
        var command = catalog.Tools.Single(t => t.Name == "execute_command");
        var args = CliToolInvoker.BindArguments(command, new Dictionary<string, string?> { ["sql"] = "insert into markers values (99)", ["database-name"] = "analytics" });
        Error(await CliToolInvoker.InvokeAsync(command, commands, args), 1010);
        var web = fixture.Host.Services.GetRequiredService<DatabaseMcpServer.Web.CliWebToolService>();
        var webResult = JsonSerializer.SerializeToElement(await web.InvokeAsync("execute_command",
            new(new Dictionary<string, JsonElement>
            {
                ["sql"] = JsonSerializer.SerializeToElement("insert into markers values (99)"),
                ["database-name"] = JsonSerializer.SerializeToElement("analytics")
            }, "execute_command"), CancellationToken.None));
        Assert.False(webResult.GetProperty("toolSuccess").GetBoolean());
        Assert.Equal(1010, webResult.GetProperty("result").GetProperty("errorCode").GetInt32());
    }

    [Fact]
    public async Task ReadWritePolicy_RejectsDdlEvenWhenDangerousOperationsEnabled()
    {
        using var fixture = Create(new() { Access = DatabaseToolAccess.Write });
        var commands = fixture.Host.Services.GetRequiredService<CommandTools>();
        Success(await commands.ExecuteCommand("insert into markers values (3)", databaseName: "analytics"));
        Error(await commands.ExecuteCommand("create table blocked (id int)", databaseName: "analytics"), 1010);
        Error(commands.CallStoredProcedure("anything", databaseName: "analytics"), 1010);
        using var list = JsonDocument.Parse("""["insert into markers values (4)","drop table markers"]""");
        var batch = Success(await commands.BatchExecuteCommands(list.RootElement.EnumerateArray().Select(item => new DatabaseCommand { Sql = item.GetString()! }).ToArray(), databaseName: "analytics"));
        Assert.True(batch.GetProperty("results")[0].GetProperty("success").GetBoolean());
        Assert.False(batch.GetProperty("results")[1].GetProperty("success").GetBoolean());
        Assert.Equal(3, Success(await fixture.Host.Services.GetRequiredService<QueryTools>().GetScalar("select count(*) from markers", databaseName: "analytics")).GetProperty("value").GetInt32());
    }

    [Fact]
    public async Task Budgets_ApplyDefaultsCapsAndSingleValueLimit()
    {
        using var fixture = Create(new() { BoundedQueryDefaults = true, DefaultQueryRows = 1, DefaultResponseBytes = 2048,
            MaxQueryRows = 2, MaxResponseBytes = 4096, MaxCommandTimeoutSeconds = 10, AllowInfiniteTimeout = false });
        var queries = fixture.Host.Services.GetRequiredService<QueryTools>();
        var structuredQueries = fixture.Host.Services.GetRequiredService<DatabaseQueryTools>();
        var limited = Success(await structuredQueries.QueryDatabase("select 1 as v union all select abs(-9223372036854775808)").Json());
        Assert.Equal(1, limited.GetProperty("rowCount").GetInt32());
        Assert.True(limited.GetProperty("limitReached").GetBoolean());
        Error(await structuredQueries.QueryDatabase("select 1", maxRows: 3).Json(), 1004);
        Error(await structuredQueries.QueryDatabase("select 1", maxResponseBytes: 4097).Json(), 1004);
        Error(await queries.GetScalar("select 1", commandTimeoutSeconds: 0), 1004);
        Error(await queries.SqlQuerySingle("select 1", commandTimeoutSeconds: 11), 1004);
        Error(await queries.GetScalar("select hex(zeroblob(10000))"), 1013);
        Error(await queries.SqlQuerySingle("select hex(zeroblob(10000)) as v"), 1013);
        Assert.Equal(7, Success(await queries.GetScalar("select 7")).GetProperty("value").GetInt32());
    }

    [Fact]
    public async Task StructuredQuery_CliBindingTypedNullAndBoundedUnicode()
    {
        using var fixture = Create();
        var metadata = new CliToolCatalog().Tools.Single(t => t.Name == "query_database");
        var args = CliToolInvoker.BindArguments(metadata, new Dictionary<string, string?>
        {
            ["sql"] = "select @nullable as nullable, @big as big", ["database-name"] = "analytics",
            ["parameters"] = """[{"name":"nullable","value":null,"dbType":"Int64"},{"name":"big","value":"9007199254740993","dbType":"Int64"}]"""
        });
        var tools = fixture.Host.Services.GetRequiredService<DatabaseQueryTools>();
        var result = Success(await CliToolInvoker.InvokeAsync(metadata, tools, args));
        Assert.Equal(JsonValueKind.Null, result.GetProperty("data")[0].GetProperty("nullable").ValueKind);
        Assert.Equal(9007199254740993L, result.GetProperty("data")[0].GetProperty("big").GetInt64());
        var bounded = await tools.QueryDatabase("select @v as v", parameters: [new() { Name = "v", Value = JsonSerializer.SerializeToElement(new string('汉', 2000)) }], maxResponseBytes: 1400);
        Assert.True(Structured(bounded).GetProperty("truncated").GetBoolean());
        Assert.Empty(Structured(bounded).GetProperty("data").EnumerateArray());
        Assert.InRange(System.Text.Encoding.UTF8.GetByteCount(((TextContentBlock)bounded.Content[0]).Text), 1, 1400);
    }

    [Fact]
    public async Task Telemetry_RecordsBalancedLeasesReadingAndPayloadBytesWithoutSqlTags()
    {
        var longs = new Dictionary<string, List<long>>();
        var durations = new HashSet<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) => { if (instrument.Meter.Name == DatabaseTelemetry.MeterName) current.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => { Assert.Empty(tags.ToArray()); if (!longs.TryGetValue(instrument.Name, out var list)) longs[instrument.Name] = list = []; list.Add(value); });
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => { Assert.Empty(tags.ToArray()); Assert.True(value >= 0); durations.Add(instrument.Name); });
        listener.Start();
        using var fixture = Create();
        var result = await fixture.Host.Services.GetRequiredService<DatabaseQueryTools>().QueryDatabase("select 1 as value");
        Assert.Equal(0, longs["dbmcp.active"].Sum());
        Assert.Contains("dbmcp.read.duration", durations);
        Assert.Contains("dbmcp.request.duration", durations);
        Assert.Contains(System.Text.Encoding.UTF8.GetByteCount(((TextContentBlock)result.Content[0]).Text), longs["dbmcp.response.bytes"]);
    }

    internal static RuntimeConsistencyTests.Fixture Create(DatabaseExecutionOptions? options = null, string? access = null)
    {
        var fixture = new RuntimeConsistencyTests.Fixture(configureServices: services => { if (options != null) services.AddSingleton(options); });
        var config = new CliConfigFileService().Load(fixture.Path);
        foreach (var db in config.Databases)
        {
            db.ConnectionString = $"Data Source={Path.Combine(fixture.Directory.FullName, db.Name + ".db")};Pooling=False";
            db.AccessMode = access;
            db.EnableDangerousOperations = true;
        }
        File.WriteAllText(fixture.Path, JsonSerializer.Serialize(config));
        Assert.True(fixture.Service.ReloadConfiguration().Success);
        foreach (var name in new[] { "primary", "analytics" })
        {
            using var lease = fixture.Service.AcquireClient(name);
            lease.Client.Ado.ExecuteCommand($"create table markers(value integer primary key); insert into markers values ({(name == "primary" ? 1 : 2)})");
        }
        return fixture;
    }

    internal static JsonElement Success(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean(), payload);
        return doc.RootElement.Clone();
    }

    internal static void Error(string payload, int code)
    {
        using var doc = JsonDocument.Parse(payload);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean(), payload);
        Assert.Equal(code, doc.RootElement.GetProperty("errorCode").GetInt32());
    }

    private static JsonElement Structured(CallToolResult result)
    {
        Assert.False(result.IsError);
        var text = Success(Assert.Single(result.Content.OfType<TextContentBlock>()).Text);
        Assert.True(JsonElement.DeepEquals(text, result.StructuredContent!.Value));
        return text;
    }
}
