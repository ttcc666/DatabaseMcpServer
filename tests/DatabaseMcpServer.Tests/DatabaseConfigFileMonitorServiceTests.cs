using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DatabaseMcpServer.Tests;

public class DatabaseConfigFileMonitorServiceTests
{
    [Fact]
    public void HandleMonitoredFileChanged_ShouldFollowNewDefaultWhenMonitorEnabled()
    {
        var configPath = WriteConfigFile("""
            {
              "enableMonitorConfig": true,
              "databases": [
                {
                  "name": "primary",
                  "connectionString": "Server=localhost;Database=main;User Id=sa;Password=secret;",
                  "dbType": "SqlServer",
                  "isDefault": true
                },
                {
                  "name": "analytics",
                  "connectionString": "Server=localhost;Database=analytics;User Id=sa;Password=secret;",
                  "dbType": "SqlServer"
                }
              ]
            }
            """);

        var originalConfigPath = Environment.GetEnvironmentVariable("DB_CONFIG_PATH");
        var originalMonitor = Environment.GetEnvironmentVariable("ENABLE_MONITOR_CONFIG");
        var stateFilePath = Path.Combine(Path.GetTempPath(), $"dbmcp-state-{Guid.NewGuid():N}.json");
        File.WriteAllText(stateFilePath, """{ "entries": [] }""");

        try
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", configPath);
            Environment.SetEnvironmentVariable("ENABLE_MONITOR_CONFIG", null);

            var helper = new DatabaseHelper(NullLogger<DatabaseHelper>.Instance);
            var service = new DatabaseConfigService(
                NullLogger<DatabaseConfigService>.Instance,
                helper,
                new NoopSqlSugarClientFactory(),
                new JsonResultSerializer(),
                new CurrentDatabaseStateStore(NullLogger<CurrentDatabaseStateStore>.Instance, enabled: true, stateFilePath));

            Assert.True(service.SwitchDatabase("analytics"));

            using var monitor = new DatabaseConfigFileMonitorService(
                service,
                NullLogger<DatabaseConfigFileMonitorService>.Instance);

            File.WriteAllText(configPath, """
                {
                  "enableMonitorConfig": true,
                  "databases": [
                    {
                      "name": "primary",
                      "connectionString": "Server=localhost;Database=main;User Id=sa;Password=secret;",
                      "dbType": "SqlServer"
                    },
                    {
                      "name": "analytics",
                      "connectionString": "Server=localhost;Database=analytics;User Id=sa;Password=secret;",
                      "dbType": "SqlServer",
                      "isDefault": true
                    }
                  ]
                }
                """);

            monitor.HandleMonitoredFileChangedForTests();

            Assert.Equal("analytics", service.GetCurrentDatabaseName());

            File.WriteAllText(configPath, """
                {
                  "enableMonitorConfig": true,
                  "databases": [
                    {
                      "name": "primary",
                      "connectionString": "Server=localhost;Database=main;User Id=sa;Password=secret;",
                      "dbType": "SqlServer",
                      "isDefault": true
                    },
                    {
                      "name": "analytics",
                      "connectionString": "Server=localhost;Database=analytics;User Id=sa;Password=secret;",
                      "dbType": "SqlServer"
                    }
                  ]
                }
                """);

            monitor.HandleMonitoredFileChangedForTests();
            Assert.Equal("primary", service.GetCurrentDatabaseName());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", originalConfigPath);
            Environment.SetEnvironmentVariable("ENABLE_MONITOR_CONFIG", originalMonitor);
            if (File.Exists(configPath))
            {
                File.Delete(configPath);
            }

            if (File.Exists(stateFilePath))
            {
                File.Delete(stateFilePath);
            }
        }
    }

    [Fact]
    public void HandleMonitoredFileChanged_ShouldNotReloadWhenMonitorDisabled()
    {
        var configPath = WriteConfigFile("""
            {
              "databases": [
                {
                  "name": "primary",
                  "connectionString": "Server=localhost;Database=main;User Id=sa;Password=secret;",
                  "dbType": "SqlServer",
                  "isDefault": true
                },
                {
                  "name": "analytics",
                  "connectionString": "Server=localhost;Database=analytics;User Id=sa;Password=secret;",
                  "dbType": "SqlServer"
                }
              ]
            }
            """);

        var originalConfigPath = Environment.GetEnvironmentVariable("DB_CONFIG_PATH");
        var originalMonitor = Environment.GetEnvironmentVariable("ENABLE_MONITOR_CONFIG");
        var stateFilePath = Path.Combine(Path.GetTempPath(), $"dbmcp-state-{Guid.NewGuid():N}.json");
        File.WriteAllText(stateFilePath, """{ "entries": [] }""");

        try
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", configPath);
            Environment.SetEnvironmentVariable("ENABLE_MONITOR_CONFIG", "false");

            var helper = new DatabaseHelper(NullLogger<DatabaseHelper>.Instance);
            var service = new DatabaseConfigService(
                NullLogger<DatabaseConfigService>.Instance,
                helper,
                new NoopSqlSugarClientFactory(),
                new JsonResultSerializer(),
                new CurrentDatabaseStateStore(NullLogger<CurrentDatabaseStateStore>.Instance, enabled: true, stateFilePath));

            Assert.True(service.SwitchDatabase("analytics"));

            using var monitor = new DatabaseConfigFileMonitorService(
                service,
                NullLogger<DatabaseConfigFileMonitorService>.Instance);

            File.WriteAllText(configPath, """
                {
                  "enableMonitorConfig": true,
                  "databases": [
                    {
                      "name": "primary",
                      "connectionString": "Server=localhost;Database=main;User Id=sa;Password=secret;",
                      "dbType": "SqlServer",
                      "isDefault": true
                    },
                    {
                      "name": "analytics",
                      "connectionString": "Server=localhost;Database=analytics;User Id=sa;Password=secret;",
                      "dbType": "SqlServer"
                    }
                  ]
                }
                """);

            monitor.HandleMonitoredFileChangedForTests();
            Assert.Equal("analytics", service.GetCurrentDatabaseName());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", originalConfigPath);
            Environment.SetEnvironmentVariable("ENABLE_MONITOR_CONFIG", originalMonitor);
            if (File.Exists(configPath))
            {
                File.Delete(configPath);
            }

            if (File.Exists(stateFilePath))
            {
                File.Delete(stateFilePath);
            }
        }
    }

    private static string WriteConfigFile(string json)
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"dbmcp-{Guid.NewGuid():N}.json");
        File.WriteAllText(configPath, json);
        return configPath;
    }

    private sealed class NoopSqlSugarClientFactory : ISqlSugarClientFactory
    {
        public SqlSugar.ISqlSugarClient CreateClient(DatabaseMcpServer.Models.DatabaseConnection connection)
        {
            throw new NotSupportedException();
        }

        public void ResetClientPool()
        {
        }
    }
}
