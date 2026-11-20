using System.Text.Json;
using System.Data;
using System.Data.Common;
using DatabaseMcpServer.Extensions;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Tools.Management;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DatabaseMcpServer.Tests;

public class PooledClientLifecycleTests
{
    [Fact]
    public async Task ToolCalls_UseFreshContextsAfterPreviousContextIsDisposed()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"dbmcp-pooled-{Guid.NewGuid():N}.db");
        var configPath = WriteSqliteConfigFile(databasePath);
        var originalConfigPath = Environment.GetEnvironmentVariable("DB_CONFIG_PATH");

        try
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", configPath);

            var builder = Host.CreateApplicationBuilder();
            builder.Services.AddDatabaseMcpApplicationServices();
            builder.Services.AddDatabaseMcpToolServices();

            using var host = builder.Build();
            var databaseConfig = host.Services.GetRequiredService<IDatabaseConfigService>();
            using var initial = databaseConfig.AcquireClient();
            var client = initial.Client;
            var executedSql = new List<string>();
            var disposed = 0;
            ((DbConnection)client.Ado.Connection).Disposed += (_, _) => disposed++;
            client.Aop.OnLogExecuting = (sql, _) => executedSql.Add(sql);
            initial.Dispose();

            var tool = host.Services.GetRequiredService<ConnectionTools>();

            AssertSuccess(await tool.TestConnection());
            AssertSuccess(await tool.TestConnection());
            Assert.Empty(executedSql);
            using var next = databaseConfig.AcquireClient();
            Assert.NotSame(client, next.Client);
            Assert.Equal(1, disposed);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", originalConfigPath);
            DeleteFileIfExists(configPath);
            DeleteFileIfExists(databasePath);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConnectionCheck_ReportsTheConnectionItOpened(bool retry)
    {
        using var directory = new TemporaryUserProfile();
        var config = Path.Combine(directory.DirectoryPath, "databases.json");
        File.WriteAllText(config, """
            {"databases":[
              {"name":"first","dbType":"sqlite","connectionString":"Data Source=:memory:","isDefault":true},
              {"name":"second","dbType":"Sqlite","connectionString":"Data Source=:memory:"}
            ]}
            """);
        var previous = Environment.GetEnvironmentVariable("DB_CONFIG_PATH");
        try
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", config);
            var builder = Host.CreateApplicationBuilder();
            builder.Services.AddDatabaseMcpApplicationServices();
            builder.Services.AddDatabaseMcpToolServices();
            IDatabaseConfigService? service = null;
            var switched = false;
            ObservedClientFactory.Register(builder.Services, client =>
            {
                ((DbConnection)client.Ado.Connection).StateChange += (_, change) =>
                {
                    if (change.CurrentState == ConnectionState.Open && !switched)
                    {
                        switched = true;
                        service!.SwitchDatabase("second");
                        if (retry) throw new InvalidOperationException("模拟第一次连接失败");
                    }
                };
            });
            using var host = builder.Build();
            service = host.Services.GetRequiredService<IDatabaseConfigService>();
            var tool = host.Services.GetRequiredService<ConnectionTools>();

            using var first = JsonDocument.Parse(retry ? await tool.TestConnectionWithRetry(1, 1) : await tool.TestConnection());
            Assert.True(first.RootElement.GetProperty("connected").GetBoolean());
            if (retry)
            {
                Assert.Equal(2, first.RootElement.GetProperty("attempts").GetInt32());
            }
            Assert.Equal("first", first.RootElement.GetProperty("currentDatabase").GetString());
            Assert.Equal("sqlite", first.RootElement.GetProperty("databaseType").GetString());
            using var second = JsonDocument.Parse(await tool.TestConnection());
            Assert.Equal("second", second.RootElement.GetProperty("currentDatabase").GetString());
            Assert.Equal("Sqlite", second.RootElement.GetProperty("databaseType").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", previous);
        }
    }

    private static void AssertSuccess(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        Assert.True(document.RootElement.GetProperty("success").GetBoolean(), payload);
        Assert.True(document.RootElement.GetProperty("connected").GetBoolean(), payload);
    }

    private static string WriteSqliteConfigFile(string databasePath)
    {
        var escapedDatabasePath = databasePath.Replace("\\", "\\\\", StringComparison.Ordinal);
        var configPath = Path.Combine(Path.GetTempPath(), $"dbmcp-pooled-config-{Guid.NewGuid():N}.json");
        File.WriteAllText(configPath, $$"""
            {
              "databases": [
                {
                  "name": "sqlite-local",
                  "connectionString": "Data Source={{escapedDatabasePath}};Cache=Shared;Mode=ReadWriteCreate;",
                  "dbType": "Sqlite",
                  "isDefault": true
                }
              ]
            }
            """);
        return configPath;
    }

    private static void DeleteFileIfExists(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
