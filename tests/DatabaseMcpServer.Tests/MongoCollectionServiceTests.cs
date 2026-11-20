using System.Text.Json;
using DatabaseMcpServer.Cli;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using DatabaseMcpServer.Tools.Management;
using DatabaseMcpServer.Web;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using static DatabaseMcpServer.Tests.MongoDocumentContractTests;

namespace DatabaseMcpServer.Tests;

public class MongoCollectionServiceTests
{
    internal static RuntimeConsistencyTests.Fixture Fixture(string? access = null, bool dangerous = false, string? uri = null,
        Action<IServiceCollection>? configure = null)
    {
        var fixture = new RuntimeConsistencyTests.Fixture(configureServices: configure);
        var config = new CliConfigFileService().Load(fixture.Path);
        var connection = config.Databases[0];
        connection.DbType = "MongoDb";
        connection.ConnectionString = uri ?? "mongodb://localhost:1/test";
        connection.AccessMode = access;
        connection.EnableDangerousOperations = dangerous;
        File.WriteAllText(fixture.Path, JsonSerializer.Serialize(config));
        Assert.True(fixture.Service.ReloadConfiguration().Success);
        return fixture;
    }

    internal static JsonElement Payload(CallToolResult result)
    {
        var data = result.StructuredContent!.Value;
        using var text = JsonDocument.Parse(Assert.Single(result.Content.OfType<TextContentBlock>()).Text);
        Assert.True(JsonElement.DeepEquals(data, text.RootElement));
        Assert.Equal(!data.GetProperty("success").GetBoolean(), result.IsError);
        return data;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadOnlyPolicy_BlocksEveryMongoWriteBeforeClientCreation(bool serverPolicy)
    {
        var calls = 0;
        using var registry = new MongoClientRegistry((_, _) => { calls++; throw new InvalidOperationException("no network allowed"); });
        using var fixture = Fixture(serverPolicy ? null : "read-only", configure: services =>
        {
            services.AddSingleton(registry);
            if (serverPolicy) services.AddSingleton(new DatabaseExecutionOptions { Access = DatabaseToolAccess.Read });
        });
        var service = fixture.Host.Services.GetRequiredService<MongoCollectionService>();
        foreach (var operation in new[] { MongoOperation.Insert, MongoOperation.Update, MongoOperation.Delete })
        {
            var error = Payload(await service.ExecuteAsync(new(operation, "docs", Object("{\"x\":1}"), Document: Object("{}"), Update: Object("{\"$set\":{\"x\":2}}")), default));
            Assert.Equal(1010, error.GetProperty("errorCode").GetInt32());
            Assert.Equal("not-executed", error.GetProperty("writeOutcome").GetString());
        }
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task InvalidDocumentsAndWriteConcern_AreRejectedBeforeClientCreation()
    {
        var calls = 0;
        using var registry = new MongoClientRegistry((_, _) => { calls++; throw new InvalidOperationException("no network allowed"); });
        using var fixture = Fixture(configure: services => services.AddSingleton(registry));
        var service = fixture.Host.Services.GetRequiredService<MongoCollectionService>();
        var cases = new[]
        {
            new MongoCollectionRequest(MongoOperation.Find, "system.users"),
            new MongoCollectionRequest(MongoOperation.Find, "docs", Object("{\"$where\":\"secret()\"}")),
            new MongoCollectionRequest(MongoOperation.Find, "docs", Skip: 100001),
            new MongoCollectionRequest(MongoOperation.Update, "docs", Object("{}"), Update: Object("{\"$set\":{\"x\":1}}")),
            new MongoCollectionRequest(MongoOperation.Update, "docs", Object("{\"x\":1}"), Update: Object("{\"$set\":{\"_id\":1}}")),
            new MongoCollectionRequest(MongoOperation.Delete, "docs"),
            new MongoCollectionRequest(MongoOperation.Insert, "docs", Document: Object(JsonSerializer.Serialize(new { _id = new string('x', 4000) })), MaxResponseBytes: 1024)
        };
        foreach (var request in cases)
        {
            var payload = Payload(await service.ExecuteAsync(request, default));
            Assert.False(payload.GetProperty("success").GetBoolean());
            if (request.IsWrite) Assert.Equal("not-executed", payload.GetProperty("writeOutcome").GetString());
        }
        var config = new CliConfigFileService().Load(fixture.Path);
        config.Databases[0].ConnectionString += "?w=0";
        File.WriteAllText(fixture.Path, JsonSerializer.Serialize(config));
        Assert.True(fixture.Service.ReloadConfiguration().Success);
        var rejected = Payload(await service.ExecuteAsync(new(MongoOperation.Insert, "docs", Document: Object("{}")), default));
        Assert.Equal(1004, rejected.GetProperty("errorCode").GetInt32());
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task MongoTools_OnSqlReturnUnsupportedAndWebRequiresConfirmation()
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        var service = fixture.Host.Services.GetRequiredService<MongoCollectionService>();
        foreach (var operation in Enum.GetValues<MongoOperation>())
            Assert.Equal(1012, Payload(await service.ExecuteAsync(new(operation, "docs"), default)).GetProperty("errorCode").GetInt32());
        var catalog = new CliToolCatalog();
        var web = fixture.Host.Services.GetRequiredService<CliWebToolService>();
        foreach (var name in new[] { "mongo_insert_one", "mongo_update", "mongo_delete" })
        {
            Assert.True(catalog.Tools.Single(t => t.Name == name).RequiresConfirmation);
            await Assert.ThrowsAsync<InvalidOperationException>(() => web.InvokeAsync(name, new(null, null), default));
        }
        var result = JsonSerializer.SerializeToElement(await web.InvokeAsync("mongo_find", new(new Dictionary<string, JsonElement>()
        { ["collection"] = JsonSerializer.SerializeToElement("docs"), ["filter"] = JsonSerializer.SerializeToElement(new { x = 1 }) }, null), default));
        Assert.Equal(1012, result.GetProperty("result").GetProperty("errorCode").GetInt32());
    }

    [Fact]
    public async Task MongoCapabilities_AreOfflineAndCancellationReleasesAdmission()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var registry = new MongoClientRegistry(async (_, token) =>
        { calls++; entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); throw new InvalidOperationException(); });
        using var fixture = Fixture(uri: "mongodb+srv://example.invalid/test", configure: s => s.AddSingleton(registry));
        using var capabilities = JsonDocument.Parse(fixture.Host.Services.GetRequiredService<ConnectionTools>().GetDatabaseCapabilities());
        Assert.True(capabilities.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(0, calls);
        var service = fixture.Host.Services.GetRequiredService<MongoCollectionService>();
        using var cancellation = new CancellationTokenSource();
        var query = service.ExecuteAsync(new(MongoOperation.Find, "docs"), cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query);
        using var lease = fixture.Service.AcquireClient();
        var timedOut = Payload(await service.ExecuteAsync(new(MongoOperation.Count, "docs", CommandTimeoutSeconds: 1), default));
        Assert.Equal(1011, timedOut.GetProperty("errorCode").GetInt32());
    }

    [Fact]
    public async Task ConnectionDiagnostics_PropagateCancellationWithoutLeakingAdmission()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registry = new MongoClientRegistry(async (_, token) =>
        { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); throw new InvalidOperationException(); });
        using var fixture = Fixture(configure: services => services.AddSingleton(registry));
        var probe = fixture.Host.Services.GetRequiredService<DatabaseConnectionProbe>();
        var handler = new CliConfigCommandHandler(new JsonResultSerializer(), new CliConfigFileService(),
            fixture.Host.Services.GetRequiredService<DatabaseMcpServer.Interfaces.ISqlSugarClientFactory>(), probe);
        using var cancellation = new CancellationTokenSource();
        var pending = handler.DoctorAsync(fixture.Path, "primary", true, false, false, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        using var next = await fixture.Service.AcquireClientAsync();
        Assert.Equal("primary", next.Context.DatabaseName);
    }

    [Fact]
    public async Task CliRejectsInvalidObjectAsUsageErrorBeforeResolvingConfig()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = await new CliRunner().RunAsync(["tool", "mongo_find", "--collection", "docs", "--filter", "null"], output, error);
        Assert.Equal(2, exit);
        Assert.Empty(output.ToString());
        Assert.Contains("JSON", error.ToString());
    }
}
