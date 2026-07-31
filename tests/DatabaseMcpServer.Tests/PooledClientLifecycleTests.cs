using System.Text.Json;
using DatabaseMcpServer.Extensions;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Tools.Management;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DatabaseMcpServer.Tests;

public class PooledClientLifecycleTests
{
    [Fact]
    public void ToolCalls_ShouldReusePooledClientWithoutDisposingIt()
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
            var client = databaseConfig.CreateClient();
            var executedSql = new List<string>();
            client.Aop.OnLogExecuting = (sql, _) => executedSql.Add(sql);

            var tool = host.Services.GetRequiredService<ConnectionTools>();

            AssertSuccess(tool.TestConnection());
            AssertSuccess(tool.TestConnection());
            Assert.Empty(executedSql);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", originalConfigPath);
            DeleteFileIfExists(configPath);
            DeleteFileIfExists(databasePath);
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
