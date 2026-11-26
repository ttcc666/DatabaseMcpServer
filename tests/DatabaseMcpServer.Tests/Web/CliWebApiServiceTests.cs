using DatabaseMcpServer.Tools;
using DatabaseMcpServer.Configuration;
using DatabaseMcpServer.Cli;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using DatabaseMcpServer.Tools.Management;
using DatabaseMcpServer.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace DatabaseMcpServer.Tests;

public class CliWebApiServiceTests
{
    [Fact]
    public void GetDashboard_DistinguishesRuntimeAndSavedSelectionAndMasksConnections()
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        Assert.True(fixture.Service.SwitchDatabase("analytics"));
        var state = fixture.Host.Services.GetRequiredService<ICurrentDatabaseStateStore>();
        state.SaveRequiredCurrentDatabaseName(fixture.Path, "primary");
        var files = new ConfigurationFileStore();
        var config = files.Load(fixture.Path);
        config.Databases[0].ConnectionString = "Server=localhost;Password=dashboard-secret;";
        File.WriteAllText(fixture.Path, JsonSerializer.Serialize(config));
        var api = CreateApi(fixture);

        var dashboard = JsonSerializer.SerializeToElement(api.GetDashboard(), JsonSerializationDefaults.CompactCamelCase);

        Assert.True(dashboard.GetProperty("success").GetBoolean());
        Assert.Equal("analytics", dashboard.GetProperty("currentDatabase").GetString());
        Assert.Equal("primary", dashboard.GetProperty("persistedCurrentDatabase").GetString());
        Assert.Equal("primary", dashboard.GetProperty("currentDefaultDatabase").GetString());
        Assert.Equal(2, dashboard.GetProperty("totalDatabases").GetInt32());
        Assert.False(dashboard.GetProperty("databases")[0].GetProperty("isCurrent").GetBoolean());
        Assert.True(dashboard.GetProperty("databases")[1].GetProperty("isCurrent").GetBoolean());
        Assert.DoesNotContain("dashboard-secret", dashboard.GetRawText());
        var database = JsonSerializer.SerializeToElement(api.GetDatabase("primary"), JsonSerializationDefaults.CompactCamelCase);
        Assert.True(database.GetProperty("success").GetBoolean());
        Assert.Equal("primary", database.GetProperty("database").GetProperty("name").GetString());
        Assert.DoesNotContain("dashboard-secret", database.GetRawText());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GetDashboard_UnreadableFileKeepsActiveRuntime(bool missing)
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        Assert.True(fixture.Service.SwitchDatabase("analytics"));
        var api = CreateApi(fixture);
        if (missing) File.Delete(fixture.Path);
        else File.WriteAllText(fixture.Path, "{");

        var dashboard = JsonSerializer.SerializeToElement(api.GetDashboard(), JsonSerializationDefaults.CompactCamelCase);

        Assert.Equal(missing, dashboard.GetProperty("success").GetBoolean());
        Assert.Equal(!missing, dashboard.GetProperty("configExists").GetBoolean());
        Assert.Equal("analytics", dashboard.GetProperty("currentDatabase").GetString());
        Assert.Equal("analytics", dashboard.GetProperty("runtime").GetProperty("currentDatabase").GetString());
        Assert.Empty(dashboard.GetProperty("databases").EnumerateArray());
        Assert.False(dashboard.TryGetProperty("persistedCurrentDatabase", out _));
    }

    [Fact]
    public void GetDashboard_RuntimeResolutionFailureStillReturnsSavedConnections()
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        using var provider = new ServiceCollection()
            .AddSingleton<IDatabaseConfigService>(_ => throw new InvalidOperationException("runtime unavailable"))
            .AddSingleton(new ConfigurationRuntimeApplier(new(), () => fixture.Service, () => { }))
            .BuildServiceProvider();
        var api = CliWebApiTestFactory.Create(new(fixture.Path, "test"), new(), new(),
            fixture.Host.Services.GetRequiredService<ICurrentDatabaseStateStore>(), new(), provider);

        var dashboard = JsonSerializer.SerializeToElement(api.GetDashboard(), JsonSerializationDefaults.CompactCamelCase);

        Assert.True(dashboard.GetProperty("success").GetBoolean());
        Assert.Equal("runtime unavailable", dashboard.GetProperty("runtimeError").GetString());
        Assert.Equal(JsonValueKind.Null, dashboard.GetProperty("runtime").ValueKind);
        Assert.Equal(2, dashboard.GetProperty("totalDatabases").GetInt32());
        Assert.All(dashboard.GetProperty("databases").EnumerateArray(),
            db => Assert.False(db.GetProperty("isCurrent").GetBoolean()));
    }

    [Theory]
    [InlineData("success")]
    [InlineData("overwrite-denied")]
    [InlineData("invalid")]
    [InlineData("empty")]
    public async Task ImportAsync_PreservesSaveAndRuntimeResultsAndCleansTemporaryFile(string scenario)
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        var original = File.ReadAllText(fixture.Path);
        var contents = scenario switch
        {
            "invalid" => "{",
            "empty" => "{\"databases\":[]}",
            _ => """{"databases":[{"name":"imported","dbType":"Sqlite","connectionString":"Data Source=:memory:","isDefault":true}]}"""
        };
        using var stream = new ObservedImportStream(Encoding.UTF8.GetBytes(contents));

        using var result = TestJson.Parse(await CreateApi(fixture).ImportAsync(
            stream, scenario != "overwrite-denied", CancellationToken.None));

        Assert.Equal(scenario == "success", result.RootElement.GetProperty("success").GetBoolean());
        Assert.NotNull(stream.DestinationPath);
        Assert.False(File.Exists(stream.DestinationPath));
        Assert.True(stream.CanRead);
        if (scenario is "success" or "empty")
        {
            Assert.True(result.RootElement.GetProperty("saved").GetBoolean());
            Assert.Equal(scenario == "success", result.RootElement.GetProperty("runtimeApplied").GetBoolean());
            Assert.Equal(scenario == "success" ? 1 : 0, new ConfigurationFileStore().Load(fixture.Path).Databases.Count);
            Assert.Equal(scenario == "success" ? "imported" : "primary", fixture.Service.GetCurrentDatabaseName());
            if (scenario == "success") Assert.True(result.RootElement.GetProperty("selectionPersisted").GetBoolean());
        }
        else
        {
            Assert.Equal(original, File.ReadAllText(fixture.Path));
            Assert.Equal("primary", fixture.Service.GetCurrentDatabaseName());
            Assert.False(result.RootElement.TryGetProperty("saved", out _));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportAsync_CopyFailureOrCancellationCleansTemporaryFileWithoutSaving(bool cancel)
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        var original = File.ReadAllText(fixture.Path);
        using var cancellation = new CancellationTokenSource();
        using var stream = new ObservedImportStream(Encoding.UTF8.GetBytes("{}"), () =>
        {
            if (cancel) cancellation.Cancel();
            else throw new IOException("simulated upload failure");
        });
        var api = CreateApi(fixture);

        if (cancel)
        {
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.ImportAsync(stream, true, cancellation.Token));
            Assert.Equal(cancellation.Token, error.CancellationToken);
        }
        else
        {
            var error = await Assert.ThrowsAsync<IOException>(() => api.ImportAsync(stream, true, cancellation.Token));
            Assert.Equal("simulated upload failure", error.Message);
        }

        Assert.NotNull(stream.DestinationPath);
        Assert.False(File.Exists(stream.DestinationPath));
        Assert.True(stream.CanRead);
        Assert.Equal(original, File.ReadAllText(fixture.Path));
        Assert.Equal("primary", fixture.Service.GetCurrentDatabaseName());
    }

    [Fact]
    public void Export_PreservesOriginalBytesAndDownloadName()
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        var contents = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(
            "{\r\n  \"databases\": [], \"custom\": \"原样导出\"\r\n}\r\n")).ToArray();
        File.WriteAllBytes(fixture.Path, contents);

        var result = CreateApi(fixture).Export();

        Assert.Equal(contents, result.Contents);
        Assert.Matches(@"^databases-\d{8}-\d{6}\.json$", result.DownloadName);
    }

    [Fact]
    public void Export_MissingFileRetainsError()
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        var api = CreateApi(fixture);
        File.Delete(fixture.Path);

        var error = Assert.Throws<InvalidOperationException>(() => api.Export());

        Assert.Equal("配置文件不存在，无法导出。", error.Message);
    }

    private static CliWebApiService CreateApi(RuntimeConsistencyTests.Fixture fixture)
        => CliWebApiTestFactory.Create(new(fixture.Path, "test"), new(), new(),
            fixture.Host.Services.GetRequiredService<ICurrentDatabaseStateStore>(), new(), fixture.Host.Services);

    private sealed class ObservedImportStream(byte[] contents, Action? beforeCopy = null) : MemoryStream(contents)
    {
        public string? DestinationPath { get; private set; }

        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            DestinationPath = Assert.IsType<FileStream>(destination).Name;
            beforeCopy?.Invoke();
            return base.CopyToAsync(destination, bufferSize, cancellationToken);
        }
    }

    [Theory]
    [InlineData(null, "primary", "analytics", "analytics")]
    [InlineData(true, "primary", "analytics", "analytics")]
    [InlineData(false, "primary", "analytics", "primary")]
    [InlineData(null, "analytics", "primary", "analytics")]
    public void SetDefaultDatabase_ShouldHonorMonitoringPolicy(
        bool? monitorOverride, string currentDatabase, string newDefault, string expectedCurrent)
    {
        using var fixture = new ConfigFixture(monitorOverride);
        Assert.True(fixture.Service.SwitchDatabase(currentDatabase));
        using var provider = new ServiceCollection()
            .AddSingleton<IDatabaseConfigService>(fixture.Service)
            .AddSingleton(new ConfigurationRuntimeApplier(new ConfigurationFileStore(), () => fixture.Service, () => { }))
            .BuildServiceProvider();
        var api = CliWebApiTestFactory.Create(
            new CliWebConfigContext(fixture.ConfigPath, "--config"),
            new ConfigurationFileStore(),
            new ConfigurationCommands(),
            fixture.StateStore,
            new ConnectionStringBuilder(),
            provider);

        using var result = TestJson.Parse(api.SetDefaultDatabase(newDefault));
        Assert.True(result.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(expectedCurrent, fixture.Service.GetCurrentDatabaseName());
        Assert.Equal(newDefault, ConfigurationFileStore.GetCurrentDefaultDatabaseName(new ConfigurationFileStore().Load(fixture.ConfigPath)));
        using var monitor = new DatabaseConfigFileMonitorService(
            fixture.Service, NullLogger<DatabaseConfigFileMonitorService>.Instance);
        monitor.HandleMonitoredFileChangedForTests();

        Assert.Equal(expectedCurrent, fixture.Service.GetCurrentDatabaseName());
    }

    [Fact]
    public void ExternalSetDefault_WithMonitoringEnabled_ShouldFollowNewDefault()
    {
        using var fixture = new ConfigFixture();
        new ConfigurationCommands().SetDefault(fixture.ConfigPath, "analytics");
        using var monitor = new DatabaseConfigFileMonitorService(
            fixture.Service, NullLogger<DatabaseConfigFileMonitorService>.Instance);
        monitor.HandleMonitoredFileChangedForTests();

        Assert.Equal("analytics", fixture.Service.GetCurrentDatabaseName());
    }

    [Fact]
    public async Task InvokeAsync_ReportsTimeSpentWaitingForAnotherTool()
    {
        using var fixture = new ConfigFixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        using var provider = new ServiceCollection().AddSingleton<DatabaseMcpServer.Interfaces.IDatabaseConfigService>(fixture.Service)
            .AddSingleton(new DatabaseExecutionOptions()).AddTransient(_ =>
        {
            if (entered.TrySetResult() && !release.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException();
            }
            return new ConnectionTools(new DatabaseMcpServer.Operations.ConnectionOperations(fixture.Service,
                NullLogger<DatabaseMcpServer.Operations.ConnectionOperations>.Instance, new DatabaseExecutionOptions(),
                new DatabaseConnectionProbe(new DatabaseExecutionOptions(), new NoopClientFactory()),
                new SqlSugarConnectionSettings(new DatabaseMcpServer.Strategies.DBSetting.DatabaseOptimizationStrategyFactory())));
        }).BuildServiceProvider();
        var tools = new CliWebToolService(new ToolCatalog(), provider);
        var request = new CliWebToolInvocationRequest(null, null);
        var first = Task.Run(() => tools.InvokeAsync("list_databases", request, CancellationToken.None));
        Task<object> queued;
        long minimumWaitMs;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            queued = tools.InvokeAsync("list_databases", request, CancellationToken.None);
            var waiting = Stopwatch.StartNew();
            await Task.Delay(30);
            minimumWaitMs = waiting.ElapsedMilliseconds;
            Assert.False(queued.IsCompleted);
        }
        finally
        {
            release.Set();
            await first;
        }

        var response = JsonSerializer.SerializeToElement(await queued);
        Assert.True(response.GetProperty("toolSuccess").GetBoolean());
        Assert.True(response.GetProperty("queueMs").GetInt64() >= minimumWaitMs);
    }

    private sealed class ConfigFixture : IDisposable
    {
        private readonly string? _originalPath = Environment.GetEnvironmentVariable("DB_CONFIG_PATH");
        private readonly string? _originalMonitor = Environment.GetEnvironmentVariable("ENABLE_MONITOR_CONFIG");
        public string ConfigPath { get; } = Path.Combine(Path.GetTempPath(), $"dbmcp-web-{Guid.NewGuid():N}.json");
        public CurrentDatabaseStateStore StateStore { get; }
        public DatabaseConfigService Service { get; }

        public ConfigFixture(bool? monitorOverride = null)
        {
            File.WriteAllText(ConfigPath, """
                {
                  "enableMonitorConfig": true,
                  "databases": [
                    {
                      "name": "primary",
                      "dbType": "Sqlite",
                      "connectionString": "Data Source=:memory:",
                      "isDefault": true
                    },
                    {
                      "name": "analytics",
                      "dbType": "Sqlite",
                      "connectionString": "Data Source=:memory:"
                    }
                  ]
                }
                """);
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", ConfigPath);
            Environment.SetEnvironmentVariable("ENABLE_MONITOR_CONFIG", null);
            StateStore = new CurrentDatabaseStateStore(
                NullLogger<CurrentDatabaseStateStore>.Instance, enabled: false);
            Service = new DatabaseConfigService(NullLogger<DatabaseConfigService>.Instance,
            new DatabaseHelper(NullLogger<DatabaseHelper>.Instance),
            new DatabaseMcpServer.Services.DatabaseExecutionCoordinator(new DatabaseMcpServer.Models.DatabaseExecutionOptions()),
            new DatabaseConfigurationApplicationService(new DatabaseMcpServer.Configuration.ConfigurationFileStore()),
            StateStore,
            new DatabaseRuntimeOptions(monitorOverride));
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", _originalPath);
            Environment.SetEnvironmentVariable("ENABLE_MONITOR_CONFIG", _originalMonitor);
            File.Delete(ConfigPath);
        }
    }

    private sealed class NoopClientFactory : ISqlSugarClientFactory
    {
        public DatabaseMcpServer.Models.DatabaseClientLease CreateClient(DatabaseMcpServer.Models.DatabaseOperationLease operation)
        {
            try { return AcquireClient(operation.Context.ToConnection()); }
            finally { operation.Dispose(); }
        }

        public DatabaseMcpServer.Models.DatabaseClientLease AcquireClient(DatabaseMcpServer.Models.DatabaseConnection connection) => throw new NotSupportedException();
        public DatabaseMcpServer.Models.DatabaseClientReservation ReserveClient(DatabaseMcpServer.Models.DatabaseConnection connection) => throw new NotSupportedException();
        public void UpdateConnections(IReadOnlyDictionary<string, DatabaseMcpServer.Models.DatabaseConnection> connections, string? previousName = null, string? newName = null)
        {
        }
    }
}
