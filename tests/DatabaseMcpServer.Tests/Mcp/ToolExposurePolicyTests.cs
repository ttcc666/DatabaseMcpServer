using System.IO.Pipelines;
using System.Text.Json;
using DatabaseMcpServer.Configuration;
using DatabaseMcpServer.Extensions;
using DatabaseMcpServer.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DatabaseMcpServer.Tests;

public class ToolExposurePolicyTests
{
    [Theory]
    [InlineData("{\"databases\":[]}")]
    [InlineData("{")]
    public async Task UnconfiguredStartup_AllowsDiscoveryAndRecoversAfterReload(string initial)
    {
        var directory = Directory.CreateTempSubdirectory("dbmcp-discovery-");
        try
        {
            var path = Path.Combine(directory.FullName, "databases.json");
            File.WriteAllText(path, initial);
            var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
            builder.Services.AddDatabaseMcpApplicationServices(configPath: path, enableMonitorConfig: false, allowUnconfigured: true);
            builder.Services.AddDatabaseMcpServer();
            builder.Services.AddSingleton(sp => new ToolExposurePolicy("analysis", sp.GetRequiredService<ConfigurationChangeSignal>()));
            using var host = builder.Build();
            var outgoing = new Pipe();
            var incoming = new Pipe();
            await using var server = McpServer.Create(new StreamServerTransport(outgoing.Reader.AsStream(), incoming.Writer.AsStream()),
                host.Services.GetRequiredService<IOptions<McpServerOptions>>().Value, serviceProvider: host.Services);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var running = server.RunAsync(timeout.Token);
            await using (var client = await McpClient.CreateAsync(new StreamClientTransport(outgoing.Writer.AsStream(), incoming.Reader.AsStream()), cancellationToken: timeout.Token))
            {
                var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
                Assert.Equal(new[] { "get_database_capabilities", "list_databases", "test_connection" }, tools.Select(t => t.Name).Order());
                var listed = await client.CallToolAsync("list_databases", cancellationToken: timeout.Token);
                Assert.Equal(0, DatabaseRoleTests.Success(listed).GetProperty("totalDatabases").GetInt32());
                Assert.True((await client.CallToolAsync("test_connection", cancellationToken: timeout.Token)).IsError);
                File.WriteAllText(path, """{"databases":[{"name":"local","dbType":"Sqlite","connectionString":"Data Source=:memory:","isDefault":true}]}""");
                Assert.True(host.Services.GetRequiredService<DatabaseMcpServer.Interfaces.IDatabaseConfigService>().ReloadConfiguration().Success);
                Assert.Equal(ToolContract.SqlAnalysis, (await client.ListToolsAsync(cancellationToken: timeout.Token)).Select(t => t.Name).Order());
            }
            timeout.Cancel();
            try { await running; } catch (OperationCanceledException) { }
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public async Task Analysis_ListsSqlToolsAndRejectsHiddenCalls_TracksProviderReload()
    {
        using var fixture = new RuntimeConsistencyTests.Fixture(configureServices: services =>
        {
            services.AddDatabaseMcpServer();
            services.AddSingleton(sp => new ToolExposurePolicy("analysis", sp.GetRequiredService<ConfigurationChangeSignal>()));
        });
        var outgoing = new Pipe();
        var incoming = new Pipe();
        var options = fixture.Host.Services.GetRequiredService<IOptions<McpServerOptions>>().Value;
        await using var server = McpServer.Create(new StreamServerTransport(outgoing.Reader.AsStream(), incoming.Writer.AsStream()),
            options, serviceProvider: fixture.Host.Services);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var run = server.RunAsync(timeout.Token);
        await using (var client = await McpClient.CreateAsync(new StreamClientTransport(outgoing.Writer.AsStream(), incoming.Reader.AsStream()), cancellationToken: timeout.Token))
        {
            Assert.True(client.ServerCapabilities.Tools?.ListChanged);
            var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var subscription = client.RegisterNotificationHandler("notifications/tools/list_changed", (_, _) =>
            {
                changed.TrySetResult();
                return ValueTask.CompletedTask;
            });
            var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
            Assert.Equal(ToolContract.SqlAnalysis, tools.Select(t => t.Name).Order());
            Assert.Contains(tools, t => t.Name == "query_database");
            Assert.DoesNotContain(tools, t => t.Name is "execute_command" or "get_scalar" or "mongo_find");
            var denied = await client.CallToolAsync("execute_command", new Dictionary<string, object?> { ["sql"] = "select 1" }, cancellationToken: timeout.Token);
            Assert.True(denied.IsError);
            Assert.Contains("未启用", denied.Content.OfType<TextContentBlock>().Single().Text);
            var before = tools.Select(t => t.Name).Order().ToArray();
            File.WriteAllText(fixture.Path, "{");
            fixture.Service.ReloadConfiguration();
            Assert.Equal(before, (await client.ListToolsAsync(cancellationToken: timeout.Token)).Select(t => t.Name).Order());
            File.WriteAllText(fixture.Path, """{"databases":[{"name":"primary","dbType":"MongoDb","connectionString":"mongodb://localhost:1/test","isDefault":true}]}""");
            fixture.Service.ReloadConfiguration();
            await changed.Task.WaitAsync(timeout.Token);
            tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
            Assert.Equal(ToolContract.MongoAnalysis, tools.Select(t => t.Name).Order());
            Assert.Contains(tools, t => t.Name == "mongo_find");
            Assert.DoesNotContain(tools, t => t.Name == "query_database");
        }
        timeout.Cancel();
        try { await run; } catch (OperationCanceledException) { }
    }

    [Fact]
    public void InvalidProfile_IsRejected() => Assert.Throws<ArgumentException>(() => new ToolExposurePolicy("anlysis"));

    [Theory]
    [InlineData("query_database", "sql", "read-only")]
    [InlineData("call_stored_procedure", "sql", "schema-admin")]
    [InlineData("mongo_update", "mongo", "read-write")]
    public async Task JsonHelp_PublishesActualSchemaAndOptionMapping(string name, string provider, string access)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(0, await new Cli.CliRunner().RunAsync(["tool", "help", name, "--json"], output, error));
        using var json = JsonDocument.Parse(output.ToString());
        var tool = json.RootElement.GetProperty("tool");
        Assert.Equal(name, tool.GetProperty("name").GetString());
        Assert.Equal(provider, tool.GetProperty("provider").GetString());
        Assert.Equal(access, tool.GetProperty("requiredAccess").GetString());
        Assert.True(tool.GetProperty("inputSchema").GetProperty("properties").TryGetProperty("databaseName", out _));
        var target = tool.GetProperty("parameters").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "databaseName");
        Assert.Equal("database-name", target.GetProperty("optionName").GetString());
        Assert.Empty(error.ToString());
    }
}
