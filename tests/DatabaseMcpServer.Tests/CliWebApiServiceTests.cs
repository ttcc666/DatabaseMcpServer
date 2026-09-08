using DatabaseMcpServer.Cli;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using DatabaseMcpServer.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace DatabaseMcpServer.Tests;

public class CliWebApiServiceTests
{
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
            .BuildServiceProvider();
        var api = new CliWebApiService(
            new CliWebConfigContext(fixture.ConfigPath, "--config"),
            new CliConfigFileService(),
            new CliConfigCommandHandler(),
            fixture.StateStore,
            new CliConnectionStringBuilder(),
            provider);

        using var result = JsonDocument.Parse(api.SetDefaultDatabase(newDefault));
        Assert.True(result.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(expectedCurrent, fixture.Service.GetCurrentDatabaseName());
        Assert.Equal(newDefault, CliConfigFileService.GetCurrentDefaultDatabaseName(new CliConfigFileService().Load(fixture.ConfigPath)));
        using var monitor = new DatabaseConfigFileMonitorService(
            fixture.Service, NullLogger<DatabaseConfigFileMonitorService>.Instance);
        monitor.HandleMonitoredFileChangedForTests();

        Assert.Equal(expectedCurrent, fixture.Service.GetCurrentDatabaseName());
    }

    [Fact]
    public void ExternalSetDefault_WithMonitoringEnabled_ShouldFollowNewDefault()
    {
        using var fixture = new ConfigFixture();
        new CliConfigCommandHandler().SetDefault(fixture.ConfigPath, "analytics");
        using var monitor = new DatabaseConfigFileMonitorService(
            fixture.Service, NullLogger<DatabaseConfigFileMonitorService>.Instance);
        monitor.HandleMonitoredFileChangedForTests();

        Assert.Equal("analytics", fixture.Service.GetCurrentDatabaseName());
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
            Service = new DatabaseConfigService(
                NullLogger<DatabaseConfigService>.Instance,
                new DatabaseHelper(NullLogger<DatabaseHelper>.Instance),
                new NoopClientFactory(),
                new JsonResultSerializer(),
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
        public SqlSugar.ISqlSugarClient CreateClient(DatabaseConnection connection) => throw new NotSupportedException();
        public void ResetClientPool()
        {
        }
    }
}
