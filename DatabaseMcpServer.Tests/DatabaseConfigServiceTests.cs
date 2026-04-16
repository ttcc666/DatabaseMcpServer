using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using System.Text.Json;

namespace DatabaseMcpServer.Tests;

public class DatabaseConfigServiceTests
{
    [Fact]
    public void GetConfigurationSummary_ShouldMaskSensitiveConnectionStringParts()
    {
        var configPath = WriteConfigFile("""
            {
              "databases": [
                {
                  "name": "default",
                  "connectionString": "Server=localhost;Database=test;User Id=sa;Password=secret;",
                  "dbType": "SqlServer",
                  "description": "default database",
                  "isDefault": true
                }
              ]
            }
            """);

        var originalConfigPath = Environment.GetEnvironmentVariable("DB_CONFIG_PATH");
        var stateFilePath = WriteStateFile("""
            {
              "entries": [
                {
                  "configPath": "__CONFIG_PATH__",
                  "currentDatabase": "default",
                  "updatedAtUtc": "2026-04-16T00:00:00+00:00"
                }
              ]
            }
            """.Replace("__CONFIG_PATH__", configPath.Replace("\\", "\\\\", StringComparison.Ordinal), StringComparison.Ordinal));

        try
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", configPath);
            var service = CreateService(new TrackingSqlSugarClientFactory(), stateFilePath);

            var summary = service.GetConfigurationSummary();

            using var document = JsonDocument.Parse(summary);
            Assert.Equal("default", document.RootElement.GetProperty("currentDatabase").GetString());
            Assert.Equal("SqlServer", document.RootElement.GetProperty("databaseType").GetString());
            Assert.Contains("Password=****", document.RootElement.GetProperty("connectionString").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", originalConfigPath);
            DeleteFileIfExists(configPath);
            DeleteFileIfExists(stateFilePath);
        }
    }

    [Fact]
    public void LoadDatabaseConnections_ShouldPreferPersistedCurrentDatabase()
    {
        var configPath = WriteConfigFile("""
            {
              "databases": [
                {
                  "name": "primary",
                  "connectionString": "Server=localhost;Database=main;User Id=sa;Password=secret;",
                  "dbType": "SqlServer",
                  "description": "primary",
                  "isDefault": true
                },
                {
                  "name": "analytics",
                  "connectionString": "Server=localhost;Database=analytics;User Id=sa;Password=secret;",
                  "dbType": "SqlServer",
                  "description": "analytics"
                }
              ]
            }
            """);

        var stateFilePath = WriteStateFile("""
            {
              "entries": [
                {
                  "configPath": "__CONFIG_PATH__",
                  "currentDatabase": "analytics",
                  "updatedAtUtc": "2026-04-16T00:00:00+00:00"
                }
              ]
            }
            """.Replace("__CONFIG_PATH__", configPath.Replace("\\", "\\\\", StringComparison.Ordinal), StringComparison.Ordinal));

        var originalConfigPath = Environment.GetEnvironmentVariable("DB_CONFIG_PATH");

        try
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", configPath);
            var service = CreateService(new TrackingSqlSugarClientFactory(), stateFilePath);

            Assert.Equal("analytics", service.GetCurrentDatabaseName());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", originalConfigPath);
            DeleteFileIfExists(configPath);
            DeleteFileIfExists(stateFilePath);
        }
    }

    [Fact]
    public void LoadDatabaseConnections_ShouldFallbackToDefaultAndRepairPersistedState_WhenSavedConnectionMissing()
    {
        var configPath = WriteConfigFile("""
            {
              "databases": [
                {
                  "name": "primary",
                  "connectionString": "Server=localhost;Database=main;User Id=sa;Password=secret;",
                  "dbType": "SqlServer",
                  "description": "primary",
                  "isDefault": true
                },
                {
                  "name": "analytics",
                  "connectionString": "Server=localhost;Database=analytics;User Id=sa;Password=secret;",
                  "dbType": "SqlServer",
                  "description": "analytics"
                }
              ]
            }
            """);

        var stateFilePath = WriteStateFile("""
            {
              "entries": [
                {
                  "configPath": "__CONFIG_PATH__",
                  "currentDatabase": "reporting",
                  "updatedAtUtc": "2026-04-16T00:00:00+00:00"
                }
              ]
            }
            """.Replace("__CONFIG_PATH__", configPath.Replace("\\", "\\\\", StringComparison.Ordinal), StringComparison.Ordinal));

        var originalConfigPath = Environment.GetEnvironmentVariable("DB_CONFIG_PATH");

        try
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", configPath);
            var service = CreateService(new TrackingSqlSugarClientFactory(), stateFilePath);

            Assert.Equal("primary", service.GetCurrentDatabaseName());

            using var document = JsonDocument.Parse(File.ReadAllText(stateFilePath));
            Assert.Equal("primary", document.RootElement.GetProperty("entries")[0].GetProperty("currentDatabase").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", originalConfigPath);
            DeleteFileIfExists(configPath);
            DeleteFileIfExists(stateFilePath);
        }
    }

    [Fact]
    public void ReloadConfiguration_ShouldKeepCurrentDatabaseWhenItStillExists()
    {
        var configPath = WriteConfigFile("""
            {
              "databases": [
                {
                  "name": "primary",
                  "connectionString": "Server=localhost;Database=main;User Id=sa;Password=secret;",
                  "dbType": "SqlServer",
                  "description": "primary",
                  "isDefault": true
                },
                {
                  "name": "analytics",
                  "connectionString": "Server=localhost;Database=analytics;User Id=sa;Password=secret;",
                  "dbType": "SqlServer",
                  "description": "analytics"
                }
              ]
            }
            """);

        var originalConfigPath = Environment.GetEnvironmentVariable("DB_CONFIG_PATH");
        var stateFilePath = WriteStateFile("""{ "entries": [] }""");

        try
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", configPath);
            var clientFactory = new TrackingSqlSugarClientFactory();
            var service = CreateService(clientFactory, stateFilePath);
            Assert.True(service.SwitchDatabase("analytics"));

            File.WriteAllText(configPath, """
                {
                  "databases": [
                    {
                      "name": "primary",
                      "connectionString": "Server=localhost;Database=main_v2;User Id=sa;Password=secret;",
                      "dbType": "SqlServer",
                      "description": "primary",
                      "isDefault": true
                    },
                    {
                      "name": "analytics",
                      "connectionString": "Server=localhost;Database=analytics_v2;User Id=sa;Password=secret;",
                      "dbType": "SqlServer",
                      "description": "analytics"
                    },
                    {
                      "name": "reporting",
                      "connectionString": "Server=localhost;Database=reporting;User Id=sa;Password=secret;",
                      "dbType": "SqlServer",
                      "description": "reporting"
                    }
                  ]
                }
                """);

            var result = service.ReloadConfiguration();

            Assert.True(result.Success);
            Assert.Equal("analytics", result.PreviousDatabase);
            Assert.Equal("analytics", result.CurrentDatabase);
            Assert.True(result.PreservedCurrentDatabase);
            Assert.Equal(3, result.TotalDatabases);
            Assert.Equal("analytics", service.GetCurrentDatabaseName());
            Assert.Equal(1, clientFactory.ResetCount);

            using var document = JsonDocument.Parse(File.ReadAllText(stateFilePath));
            Assert.Equal("analytics", document.RootElement.GetProperty("entries")[0].GetProperty("currentDatabase").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", originalConfigPath);
            DeleteFileIfExists(configPath);
            DeleteFileIfExists(stateFilePath);
        }
    }

    [Fact]
    public void ReloadConfiguration_ShouldFallbackToDefaultDatabaseWhenCurrentDatabaseIsRemoved()
    {
        var configPath = WriteConfigFile("""
            {
              "databases": [
                {
                  "name": "primary",
                  "connectionString": "Server=localhost;Database=main;User Id=sa;Password=secret;",
                  "dbType": "SqlServer",
                  "description": "primary",
                  "isDefault": true
                },
                {
                  "name": "analytics",
                  "connectionString": "Server=localhost;Database=analytics;User Id=sa;Password=secret;",
                  "dbType": "SqlServer",
                  "description": "analytics"
                }
              ]
            }
            """);

        var originalConfigPath = Environment.GetEnvironmentVariable("DB_CONFIG_PATH");
        var stateFilePath = WriteStateFile("""{ "entries": [] }""");

        try
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", configPath);
            var clientFactory = new TrackingSqlSugarClientFactory();
            var service = CreateService(clientFactory, stateFilePath);
            Assert.True(service.SwitchDatabase("analytics"));

            File.WriteAllText(configPath, """
                {
                  "databases": [
                    {
                      "name": "archive",
                      "connectionString": "Server=localhost;Database=archive;User Id=sa;Password=secret;",
                      "dbType": "SqlServer",
                      "description": "archive"
                    },
                    {
                      "name": "primary",
                      "connectionString": "Server=localhost;Database=main_v2;User Id=sa;Password=secret;",
                      "dbType": "SqlServer",
                      "description": "primary",
                      "isDefault": true
                    }
                  ]
                }
                """);

            var result = service.ReloadConfiguration();

            Assert.True(result.Success);
            Assert.Equal("analytics", result.PreviousDatabase);
            Assert.Equal("primary", result.CurrentDatabase);
            Assert.False(result.PreservedCurrentDatabase);
            Assert.Equal("primary", service.GetCurrentDatabaseName());
            Assert.Equal(1, clientFactory.ResetCount);

            using var document = JsonDocument.Parse(File.ReadAllText(stateFilePath));
            Assert.Equal("primary", document.RootElement.GetProperty("entries")[0].GetProperty("currentDatabase").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", originalConfigPath);
            DeleteFileIfExists(configPath);
            DeleteFileIfExists(stateFilePath);
        }
    }

    [Fact]
    public void ReloadConfiguration_ShouldKeepExistingConfigurationWhenRefreshFails()
    {
        var configPath = WriteConfigFile("""
            {
              "databases": [
                {
                  "name": "primary",
                  "connectionString": "Server=localhost;Database=main;User Id=sa;Password=secret;",
                  "dbType": "SqlServer",
                  "description": "primary",
                  "isDefault": true
                }
              ]
            }
            """);

        var originalConfigPath = Environment.GetEnvironmentVariable("DB_CONFIG_PATH");
        var stateFilePath = WriteStateFile("""{ "entries": [] }""");

        try
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", configPath);
            var clientFactory = new TrackingSqlSugarClientFactory();
            var service = CreateService(clientFactory, stateFilePath);

            File.WriteAllText(configPath, "{");

            var result = service.ReloadConfiguration();

            Assert.False(result.Success);
            Assert.Equal("primary", result.PreviousDatabase);
            Assert.Equal("primary", result.CurrentDatabase);
            Assert.True(result.PreservedCurrentDatabase);
            Assert.Equal("primary", service.GetCurrentDatabaseName());
            Assert.Single(service.GetAllConnections());
            Assert.Equal(0, clientFactory.ResetCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", originalConfigPath);
            DeleteFileIfExists(configPath);
            DeleteFileIfExists(stateFilePath);
        }
    }

    private static DatabaseConfigService CreateService(TrackingSqlSugarClientFactory clientFactory, string? stateFilePath = null)
    {
        var helper = new DatabaseHelper(NullLogger<DatabaseHelper>.Instance);
        var serializer = new JsonResultSerializer();
        var stateStore = new CurrentDatabaseStateStore(
            NullLogger<CurrentDatabaseStateStore>.Instance,
            enabled: !string.IsNullOrWhiteSpace(stateFilePath),
            stateFilePath);
        return new DatabaseConfigService(
            NullLogger<DatabaseConfigService>.Instance,
            helper,
            clientFactory,
            serializer,
            stateStore);
    }

    private static string WriteConfigFile(string json)
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"dbmcp-{Guid.NewGuid():N}.json");
        File.WriteAllText(configPath, json);
        return configPath;
    }

    private static string WriteStateFile(string json)
    {
        var stateFilePath = Path.Combine(Path.GetTempPath(), $"dbmcp-state-{Guid.NewGuid():N}.json");
        File.WriteAllText(stateFilePath, json);
        return stateFilePath;
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed class TrackingSqlSugarClientFactory : ISqlSugarClientFactory
    {
        public int ResetCount { get; private set; }

        public ISqlSugarClient CreateClient(DatabaseConnection connection)
        {
            throw new NotSupportedException("这些测试不需要创建数据库客户端。");
        }

        public void ResetClientPool()
        {
            ResetCount++;
        }
    }
}
