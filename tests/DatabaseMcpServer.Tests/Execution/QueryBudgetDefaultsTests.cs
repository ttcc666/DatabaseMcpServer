using System.Text;
using System.Text.Json;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Cli;
using DatabaseMcpServer.Configuration;
using DatabaseMcpServer.Tools;
using DatabaseMcpServer.Web;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Tools.Query;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseMcpServer.Tests;

public class QueryBudgetDefaultsTests
{
    private const string Rows = "WITH RECURSIVE numbers(value) AS (SELECT 1 UNION ALL SELECT value+1 FROM numbers WHERE value<1002) SELECT value FROM numbers";

    [Theory]
    [InlineData(null, 1000)]
    [InlineData("true", 1000)]
    [InlineData("false", 1002)]
    public async Task OmittedLimits_RespectEnvironmentDefaultAndExplicitOptOut(string? setting, int expectedRows)
    {
        var previous = Environment.GetEnvironmentVariable("DB_QUERY_BOUNDED_DEFAULTS");
        try
        {
            Environment.SetEnvironmentVariable("DB_QUERY_BOUNDED_DEFAULTS", setting);
            using var fixture = new RuntimeConsistencyTests.Fixture();
            var tools = fixture.Host.Services.GetRequiredService<QueryTools>();
            using var batch = JsonDocument.Parse(await tools.BatchSqlQuery([new() { Sql = Rows }, new() { Sql = "select 7 as value" }]));
            var results = batch.RootElement.GetProperty("results");
            Assert.Equal(expectedRows, results[0].GetProperty("rowCount").GetInt32());
            Assert.Equal(expectedRows, results[0].GetProperty("data")[expectedRows - 1].GetProperty("value").GetInt32());
            Assert.Equal(setting == "false", results[1].GetProperty("executed").GetBoolean());
            Assert.Equal(setting != "false", batch.RootElement.GetProperty("limitReached").GetBoolean());

            using var multiple = JsonDocument.Parse(await tools.GetDataSetAll(Rows + "; select 7 as value"));
            Assert.Equal(expectedRows + (setting == "false" ? 1 : 0), multiple.RootElement.GetProperty("rowCount").GetInt32());
            Assert.Equal(setting == "false" ? 2 : 1, multiple.RootElement.GetProperty("resultSetCount").GetInt32());
            if (setting != "false") Assert.Equal(JsonValueKind.Null, multiple.RootElement.GetProperty("truncated").ValueKind);

            var structured = await fixture.Host.Services.GetRequiredService<DatabaseQueryTools>().QueryDatabase(Rows);
            Assert.Equal(1000, structured.StructuredContent!.Value.GetProperty("rowCount").GetInt32());
        }
        finally { Environment.SetEnvironmentVariable("DB_QUERY_BOUNDED_DEFAULTS", previous); }
    }

    [Fact]
    public async Task DefaultByteBudget_StopsWholeRowsAndRejectsInsufficientMetadataBudget()
    {
        using var fixture = new RuntimeConsistencyTests.Fixture(configureServices: services =>
            services.AddSingleton(new DatabaseExecutionOptions { DefaultResponseBytes = 2048 }));
        var tools = fixture.Host.Services.GetRequiredService<QueryTools>();
        var text = await tools.BatchSqlQuery([new() { Sql = "select '汉😀' as value union all select hex(zeroblob(2000))" }]);
        using var batch = JsonDocument.Parse(text);
        Assert.True(batch.RootElement.GetProperty("success").GetBoolean());
        Assert.True(batch.RootElement.GetProperty("truncated").GetBoolean());
        Assert.Equal("汉😀", Assert.Single(batch.RootElement.GetProperty("results")[0].GetProperty("data").EnumerateArray()).GetProperty("value").GetString());
        Assert.InRange(Encoding.UTF8.GetByteCount(text), 1, 2048);
        using var invalid = JsonDocument.Parse(await tools.BatchSqlQuery([new() { Sql = "select 1" }], maxResponseBytes: 256));
        Assert.False(invalid.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(1004, invalid.RootElement.GetProperty("errorCode").GetInt32());
    }

    [Fact]
    public async Task ExplicitLimits_OverrideDefaultsAndRemainSubjectToServerCaps()
    {
        using var fixture = new RuntimeConsistencyTests.Fixture(configureServices: services =>
            services.AddSingleton(new DatabaseExecutionOptions { DefaultQueryRows = 1, MaxQueryRows = 3 }));
        var tools = fixture.Host.Services.GetRequiredService<QueryTools>();
        using var multiple = JsonDocument.Parse(await tools.GetDataSetAll("select 1 as value; select 2 as value; select 3 as value", maxRows: 2));
        Assert.Equal(2, multiple.RootElement.GetProperty("rowCount").GetInt32());
        Assert.Equal(2, multiple.RootElement.GetProperty("resultSetCount").GetInt32());
        using var invalid = JsonDocument.Parse(await tools.GetDataSetAll("select 1", maxRows: 4));
        Assert.Equal(1004, invalid.RootElement.GetProperty("errorCode").GetInt32());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void QueryDefaults_DoNotChangeCommandTimeoutPolicy(bool bounded)
    {
        var options = new DatabaseExecutionOptions { BoundedQueryDefaults = bounded };
        Assert.Null(options.ResolveTimeout(null));
        Assert.Equal(17, options.ResolveTimeout(17));
        Assert.Equal(0, options.ResolveTimeout(0));
        Assert.Equal(12, (options with { MaxCommandTimeoutSeconds = 12 }).ResolveTimeout(null));
        Assert.Throws<DatabaseMcpException>(() => (options with { AllowInfiniteTimeout = false }).ResolveTimeout(0));
        Assert.Equal(bounded ? 30 : null, options.ResolveQuery(null, null, null).TimeoutSeconds);
    }

    [Fact]
    public void SynchronousSchemaCommand_PreservesProviderTimeoutAndHonorsServerCeiling()
    {
        foreach (var ceiling in new[] { 86400, 12 })
        {
            var observedTimeouts = new List<int>();
            using var fixture = new RuntimeConsistencyTests.Fixture(observeClient: client =>
            {
                client.Ado.CommandTimeOut = 77;
                client.Aop.OnLogExecuting = (_, _) => observedTimeouts.Add(client.Ado.CommandTimeOut);
            }, configureServices: services => services.AddSingleton(new DatabaseExecutionOptions
            {
                MaxCommandTimeoutSeconds = ceiling
            }));
            var tools = fixture.Host.Services.GetRequiredService<DatabaseMcpServer.Tools.Management.SchemaTools>();
            using var result = JsonDocument.Parse(tools.CreateTable("timeout_probe", [new() { Name = "id", DataType = "int" }]));
            Assert.True(result.RootElement.GetProperty("success").GetBoolean(), result.RootElement.ToString());
            Assert.NotEmpty(observedTimeouts);
            Assert.All(observedTimeouts, timeout => Assert.Equal(ceiling == 12 ? 12 : 77, timeout));
        }
    }

    [Fact]
    public async Task CompatibilityOptOut_KeepsTablePagingByteBudget()
    {
        using var fixture = new RuntimeConsistencyTests.Fixture(configureServices: services =>
            services.AddSingleton(new DatabaseExecutionOptions { BoundedQueryDefaults = false, DefaultResponseBytes = 2048 }));
        // Keep setup and query on a file database because paging acquires its own client.
        var path = System.IO.Path.Combine(fixture.Directory.FullName, "paging.db");
        Assert.True(fixture.Service.UpdateDatabase("analytics", new() { ApplyConnectionString = true, ConnectionString = "Data Source=" + path + ";Pooling=False" }).Success);
        using (var setup = fixture.Sessions.AcquireClient("analytics"))
        {
            setup.Client.Ado.ExecuteCommand("create table items (id integer primary key, payload text)");
            setup.Client.Ado.ExecuteCommand("insert into items values (1, hex(zeroblob(2000)))");
        }
        var response = await fixture.Host.Services.GetRequiredService<QueryTools>().QueryTablePage("items", databaseName: "analytics");
        Assert.True(response.IsError);
        Assert.Equal(1013, response.StructuredContent!.Value.GetProperty("errorCode").GetInt32());
    }

    [Fact]
    public async Task CliAndWeb_ApplyOmittedBatchBudget()
    {
        using var fixture = new RuntimeConsistencyTests.Fixture(configureServices: services =>
            services.AddSingleton(new DatabaseExecutionOptions { DefaultQueryRows = 2 }));
        var web = fixture.Host.Services.GetRequiredService<CliWebToolService>();
        var commands = new DatabaseCommand[] { new() { Sql = Rows }, new() { Sql = "select 7 as value" } };
        var response = JsonSerializer.SerializeToElement(await web.InvokeAsync("batch_sql_query",
            new(new Dictionary<string, JsonElement> { ["commands"] = JsonSerializer.SerializeToElement(commands, new JsonSerializerOptions(JsonSerializerDefaults.Web)) }, null), default));
        Assert.True(response.GetProperty("toolSuccess").GetBoolean());
        Assert.Equal(2, response.GetProperty("result").GetProperty("rowCount").GetInt32());
        Assert.False(response.GetProperty("result").GetProperty("results")[1].GetProperty("executed").GetBoolean());

        var previous = Environment.GetEnvironmentVariable("DB_QUERY_BOUNDED_DEFAULTS");
        try
        {
            Environment.SetEnvironmentVariable("DB_QUERY_BOUNDED_DEFAULTS", null);
            var runner = new CliRunner(new ToolCatalog(), new ConfigurationCommands(), new CliWebHost(), fixture.StatePath, false);
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.Equal(0, await runner.RunAsync(["tool", "get_data_set_all", "--sql", Rows, "--config", fixture.Path], output, error));
            using var payload = JsonDocument.Parse(output.ToString());
            Assert.Equal(1000, payload.RootElement.GetProperty("rowCount").GetInt32());
            Assert.True(payload.RootElement.GetProperty("limitReached").GetBoolean());
        }
        finally { Environment.SetEnvironmentVariable("DB_QUERY_BOUNDED_DEFAULTS", previous); }
    }
}
