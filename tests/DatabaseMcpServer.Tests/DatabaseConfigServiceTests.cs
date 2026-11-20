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
    public void AcquireClient_ShouldBindClientAndPolicyToSameConnection()
    {
        var configPath = WriteConfigFile("""
            {
              "databases": [
                {
                  "name": "primary",
                  "connectionString": "Server=localhost;Database=main;User Id=sa;Password=secret;",
                  "dbType": "SqlServer",
                  "isDefault": true,
                  "enableDangerousOperations": false
                },
                {
                  "name": "maintenance",
                  "connectionString": "Server=localhost;Database=maintenance;User Id=sa;Password=secret;",
                  "dbType": "SqlServer",
                  "enableDangerousOperations": true
                }
              ]
            }
            """);

        var originalConfigPath = Environment.GetEnvironmentVariable("DB_CONFIG_PATH");
        var stateFilePath = WriteStateFile("""{ "entries": [] }""");

        try
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", configPath);
            using var clientFactory = new ContextTrackingSqlSugarClientFactory();
            var service = CreateService(clientFactory, stateFilePath);

            using var primaryLease = service.AcquireClient();
            var primaryContext = primaryLease.Context;
            Assert.False(primaryContext.EnableDangerousOperations);
            Assert.Equal("primary", clientFactory.GetConnectionName(primaryContext.Client));

            Assert.True(service.SwitchDatabase("maintenance"));
            using var maintenanceLease = service.AcquireClient();
            var maintenanceContext = maintenanceLease.Context;
            Assert.True(maintenanceContext.EnableDangerousOperations);
            Assert.Equal("maintenance", clientFactory.GetConnectionName(maintenanceContext.Client));

            using var document = JsonDocument.Parse(service.GetConfigurationSummary());
            Assert.True(document.RootElement.GetProperty("enableDangerousOperations").GetBoolean());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", originalConfigPath);
            DeleteFileIfExists(configPath);
            DeleteFileIfExists(stateFilePath);
        }
    }

    [Fact]
    public void IsEnableMonitorConfigEnabled_ShouldHonorProcessOverrideOverFileAndEnvironment()
    {
        var configPath = WriteConfigFile("""
            {
              "enableMonitorConfig": false,
              "databases": [
                {
                  "name": "primary",
                  "connectionString": "Server=localhost;Database=main;User Id=sa;Password=secret;",
                  "dbType": "SqlServer",
                  "isDefault": true
                }
              ]
            }
            """);

        var originalConfigPath = Environment.GetEnvironmentVariable("DB_CONFIG_PATH");
        var originalMonitor = Environment.GetEnvironmentVariable("ENABLE_MONITOR_CONFIG");
        var stateFilePath = WriteStateFile("""{ "entries": [] }""");

        try
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", configPath);
            Environment.SetEnvironmentVariable("ENABLE_MONITOR_CONFIG", "false");
            var disabled = CreateService(new TrackingSqlSugarClientFactory(), stateFilePath);
            Assert.False(disabled.IsEnableMonitorConfigEnabled());

            var enabled = CreateService(
                new TrackingSqlSugarClientFactory(),
                stateFilePath,
                new DatabaseRuntimeOptions(EnableMonitorConfig: true));
            Assert.True(enabled.IsEnableMonitorConfigEnabled());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", originalConfigPath);
            Environment.SetEnvironmentVariable("ENABLE_MONITOR_CONFIG", originalMonitor);
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
            Assert.Equal(2, clientFactory.ResetCount);

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
            Assert.Equal(2, clientFactory.ResetCount);

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
            Assert.Equal(1, clientFactory.ResetCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", originalConfigPath);
            DeleteFileIfExists(configPath);
            DeleteFileIfExists(stateFilePath);
        }
    }

    [Fact]
    public void ReloadConfiguration_ShouldKeepExistingConfiguration_WhenExplicitConfigPathIsDeleted()
    {
        var configPath = WriteConfigFile("""
            {
              "databases": [
                {
                  "name": "primary",
                  "connectionString": "Server=localhost;Database=main;User Id=sa;Password=secret;",
                  "dbType": "SqlServer",
                  "isDefault": true
                }
              ]
            }
            """);

        var originalConfigPath = Environment.GetEnvironmentVariable("DB_CONFIG_PATH");
        using var userProfile = new TemporaryUserProfile();
        var userProfileDir = userProfile.DirectoryPath;
        var userConfigPath = Path.Combine(userProfileDir, ".database-mcp", "databases.json");
        var stateFilePath = WriteStateFile("""{ "entries": [] }""");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(userConfigPath)!);
            File.WriteAllText(userConfigPath, """
                { "databases": [ { "name": "user-profile", "connectionString": "Server=up", "dbType": "SqlServer", "isDefault": true } ] }
                """);

            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", configPath);
            var service = CreateService(new TrackingSqlSugarClientFactory(), stateFilePath, userProfileDirectory: userProfileDir);

            File.Delete(configPath);

            var result = service.ReloadConfiguration();

            Assert.False(result.Success);
            Assert.Equal("primary", service.GetCurrentDatabaseName());
            Assert.DoesNotContain(service.GetAllConnections(), item => item.Name == "user-profile");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", originalConfigPath);
            DeleteFileIfExists(configPath);
            DeleteFileIfExists(stateFilePath);
        }
    }

    [Fact]
    public void ReloadConfiguration_FollowFileDefault_ShouldSwitchCurrentWhenDefaultChanges()
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
        var stateFilePath = WriteStateFile("""{ "entries": [] }""");

        try
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", configPath);
            var service = CreateService(new TrackingSqlSugarClientFactory(), stateFilePath);
            Assert.True(service.SwitchDatabase("analytics"));

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

            var preserved = service.ReloadConfiguration();
            Assert.True(preserved.Success);
            Assert.Equal("analytics", preserved.CurrentDatabase);
            Assert.True(preserved.PreservedCurrentDatabase);

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

            var followed = service.ReloadConfiguration(followFileDefault: true);

            Assert.True(followed.Success);
            Assert.Equal("analytics", followed.PreviousDatabase);
            Assert.Equal("primary", followed.CurrentDatabase);
            Assert.False(followed.PreservedCurrentDatabase);
            Assert.Equal("primary", service.GetCurrentDatabaseName());
            Assert.True(service.IsEnableMonitorConfigEnabled());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", originalConfigPath);
            DeleteFileIfExists(configPath);
            DeleteFileIfExists(stateFilePath);
        }
    }

    [Fact]
    public void ReloadConfiguration_FollowFileDefault_ShouldKeepCurrentWhenDefaultUnchanged()
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
        var stateFilePath = WriteStateFile("""{ "entries": [] }""");

        try
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", configPath);
            var service = CreateService(new TrackingSqlSugarClientFactory(), stateFilePath);
            Assert.True(service.SwitchDatabase("analytics"));

            File.WriteAllText(configPath, """
                {
                  "databases": [
                    {
                      "name": "primary",
                      "connectionString": "Server=localhost;Database=main_v2;User Id=sa;Password=secret;",
                      "dbType": "SqlServer",
                      "isDefault": true
                    },
                    {
                      "name": "analytics",
                      "connectionString": "Server=localhost;Database=analytics_v2;User Id=sa;Password=secret;",
                      "dbType": "SqlServer"
                    }
                  ]
                }
                """);

            var result = service.ReloadConfiguration(followFileDefault: true);

            Assert.True(result.Success);
            Assert.Equal("analytics", result.CurrentDatabase);
            Assert.True(result.PreservedCurrentDatabase);
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
    public void IsEnableMonitorConfigEnabled_ShouldPreferEnvironmentOverride()
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
                }
              ]
            }
            """);

        var originalConfigPath = Environment.GetEnvironmentVariable("DB_CONFIG_PATH");
        var originalWatch = Environment.GetEnvironmentVariable("ENABLE_MONITOR_CONFIG");
        var stateFilePath = WriteStateFile("""{ "entries": [] }""");

        try
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", configPath);
            Environment.SetEnvironmentVariable("ENABLE_MONITOR_CONFIG", null);
            var service = CreateService(new TrackingSqlSugarClientFactory(), stateFilePath);
            Assert.True(service.IsEnableMonitorConfigEnabled());

            Environment.SetEnvironmentVariable("ENABLE_MONITOR_CONFIG", "false");
            Assert.False(service.IsEnableMonitorConfigEnabled());

            Environment.SetEnvironmentVariable("ENABLE_MONITOR_CONFIG", "true");
            Assert.True(service.IsEnableMonitorConfigEnabled());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", originalConfigPath);
            Environment.SetEnvironmentVariable("ENABLE_MONITOR_CONFIG", originalWatch);
            DeleteFileIfExists(configPath);
            DeleteFileIfExists(stateFilePath);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reload_PreservesSwitchDuringFileRead(bool followFileDefault)
    {
        using var directory = new TemporaryUserProfile();
        var config = Path.Combine(directory.DirectoryPath, "databases.json");
        var state = Path.Combine(directory.DirectoryPath, "cli-state.json");
        File.WriteAllText(config, """{"databases":[{"name":"primary","dbType":"Sqlite","connectionString":"Data Source=:memory:","isDefault":true},{"name":"analytics","dbType":"Sqlite","connectionString":"Data Source=:memory:"}]}""");
        var originalPath = Environment.GetEnvironmentVariable("DB_CONFIG_PATH");
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var resume = new ManualResetEventSlim();
        var reads = 0;
        string ReadConfig(string path)
        {
            var json = ConfigFileReader.ReadAllText(path);
            if (Interlocked.Increment(ref reads) == 2)
            {
                reading.SetResult();
                if (!resume.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException();
                }
            }
            return json;
        }

        try
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", config);
            var service = CreateService(new TrackingSqlSugarClientFactory(), state, readConfig: ReadConfig);
            var reload = Task.Run(() => service.ReloadConfiguration(followFileDefault));
            try
            {
                await reading.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.True(service.SwitchDatabase("analytics"));
            }
            finally
            {
                resume.Set();
            }

            var result = await reload;
            Assert.True(result.Success);
            Assert.True(result.PreservedCurrentDatabase);
            Assert.Equal("analytics", result.PreviousDatabase);
            Assert.Equal("analytics", service.GetCurrentDatabaseName());
            Assert.Equal("analytics", CreateService(new TrackingSqlSugarClientFactory(), state).GetCurrentDatabaseName());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", originalPath);
        }
    }

    [Fact]
    public async Task OverlappingReloads_PublishTheNewestConnection()
    {
        using var directory = new TemporaryUserProfile();
        var config = Path.Combine(directory.DirectoryPath, "databases.json");
        static string Version(string name) => $$"""{"databases":[{"name":"db","dbType":"Sqlite","connectionString":"Data Source={{name}}.db"}]}""";
        File.WriteAllText(config, Version("original"));
        var previous = Environment.GetEnvironmentVariable("DB_CONFIG_PATH");
        using var release = new ManualResetEventSlim();
        var firstRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondAttempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var newerRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        string ReadConfig(string path)
        {
            var content = ConfigFileReader.ReadAllText(path);
            switch (Interlocked.Increment(ref reads))
            {
                case 2:
                    firstRead.SetResult();
                    if (!release.Wait(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException();
                    }
                    break;
                case 3:
                    newerRead.SetResult();
                    break;
            }
            return content;
        }

        try
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", config);
            var service = CreateService(new TrackingSqlSugarClientFactory(), readConfig: ReadConfig);
            File.WriteAllText(config, Version("older"));
            var older = Task.Run(() => service.ReloadConfiguration());
            await firstRead.Task.WaitAsync(TimeSpan.FromSeconds(10));
            File.WriteAllText(config, Version("newer"));
            var newer = Task.Run(() =>
            {
                secondAttempt.SetResult();
                return service.ReloadConfiguration(followFileDefault: true);
            });
            await secondAttempt.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var completed = await Task.WhenAny(newerRead.Task, Task.Delay(150));
            release.Set();
            var results = await Task.WhenAll(older, newer);

            Assert.NotSame(newerRead.Task, completed);
            Assert.All(results, result => Assert.True(result.Success));
            Assert.Equal("Data Source=newer.db", service.GetConnectionString());
        }
        finally
        {
            release.Set();
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", previous);
        }
    }

    [Fact]
    public void FailedSwitch_KeepsLiveSelection()
    {
        using var directory = new TemporaryUserProfile();
        var config = Path.Combine(directory.DirectoryPath, "databases.json");
        var state = Path.Combine(directory.DirectoryPath, "cli-state.json");
        File.WriteAllText(config, """{"databases":[{"name":"primary","dbType":"Sqlite","connectionString":"Data Source=:memory:","isDefault":true},{"name":"analytics","dbType":"Sqlite","connectionString":"Data Source=:memory:"}]}""");
        var originalPath = Environment.GetEnvironmentVariable("DB_CONFIG_PATH");
        try
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", config);
            var service = CreateService(new TrackingSqlSugarClientFactory(), state);
            File.WriteAllText(state, "{");
            Assert.Throws<InvalidOperationException>(() => service.SwitchDatabase("analytics"));
            Assert.Equal("primary", service.GetCurrentDatabaseName());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", originalPath);
        }
    }

    private static DatabaseConfigService CreateService(
        ISqlSugarClientFactory clientFactory,
        string? stateFilePath = null,
        DatabaseRuntimeOptions? runtimeOptions = null,
        string? userProfileDirectory = null,
        Func<string, string>? readConfig = null)
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
            stateStore,
            runtimeOptions,
            userProfileDirectory,
            readConfig);
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

        public DatabaseMcpServer.Models.DatabaseClientLease AcquireClient(DatabaseMcpServer.Models.DatabaseConnection connection) => throw new NotSupportedException();
        public DatabaseMcpServer.Models.DatabaseClientReservation ReserveClient(DatabaseMcpServer.Models.DatabaseConnection connection) => throw new NotSupportedException();

        public void UpdateConnections(IReadOnlyDictionary<string, DatabaseMcpServer.Models.DatabaseConnection> connections, string? previousName = null, string? newName = null)
        {
            ResetCount++;
        }
    }

    private sealed class ContextTrackingSqlSugarClientFactory : ISqlSugarClientFactory, IDisposable
    {
        private readonly Dictionary<ISqlSugarClient, string> _connectionNames = [];

        public DatabaseClientLease AcquireClient(DatabaseConnection connection)
        {
            var client = new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = connection.ConnectionString,
                DbType = DbType.SqlServer,
                IsAutoCloseConnection = true
            });

            _connectionNames.Add(client, connection.Name);
            return new(new DatabaseClientContext(client, connection.EnableDangerousOperations, connection.Name), client.Dispose);
        }

        public DatabaseClientReservation ReserveClient(DatabaseConnection connection) => new(
            token => { token.ThrowIfCancellationRequested(); return AcquireClient(connection); },
            token => { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(AcquireClient(connection)); }, () => { });

        public string GetConnectionName(ISqlSugarClient client)
            => _connectionNames[client];

        public void UpdateConnections(IReadOnlyDictionary<string, DatabaseMcpServer.Models.DatabaseConnection> connections, string? previousName = null, string? newName = null)
        {
        }

        public void Dispose()
        {
            foreach (var client in _connectionNames.Keys.OfType<IDisposable>())
            {
                client.Dispose();
            }
        }
    }
}
