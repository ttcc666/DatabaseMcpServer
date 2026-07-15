using System.Text.Json;
using DatabaseMcpServer.Extensions;
using DatabaseMcpServer.Tools.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DatabaseMcpServer.Tests;

public class QueryToolsTests
{
    [Fact]
    public void BatchSqlQuery_ShouldExecuteFiveReadOnlyQueries()
    {
        var payload = Invoke(tool =>
        {
            using var queries = JsonDocument.Parse("""
                ["select 1", "select 2", "select 3", "select 4", "select 5"]
                """);
            return tool.BatchSqlQuery(queries.RootElement);
        });

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean(), payload);
        Assert.Equal(5, root.GetProperty("totalQueries").GetInt32());
        Assert.Equal(5, root.GetProperty("successfulQueries").GetInt32());
        Assert.Equal(0, root.GetProperty("failedQueries").GetInt32());
        Assert.Equal(5, root.GetProperty("results").GetArrayLength());
    }

    [Fact]
    public void BatchSqlQuery_ShouldRejectMoreThanFiveQueries()
    {
        var payload = Invoke(tool =>
        {
            using var queries = JsonDocument.Parse("""
                ["select 1", "select 2", "select 3", "select 4", "select 5", "select 6"]
                """);
            return tool.BatchSqlQuery(queries.RootElement);
        });

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean(), payload);
        Assert.Contains("一次最多允许执行 5 条查询", root.GetProperty("errorMessage").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void BatchSqlQuery_ShouldReportUnsafeQueryAndContinue()
    {
        var payload = Invoke(tool =>
        {
            using var queries = JsonDocument.Parse("""
                ["select 1", "drop table users", "select 2"]
                """);
            return tool.BatchSqlQuery(queries.RootElement);
        });

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean(), payload);
        Assert.Equal(3, root.GetProperty("totalQueries").GetInt32());
        Assert.Equal(2, root.GetProperty("successfulQueries").GetInt32());
        Assert.Equal(1, root.GetProperty("failedQueries").GetInt32());

        var results = root.GetProperty("results");
        Assert.True(results[0].GetProperty("success").GetBoolean());
        Assert.False(results[1].GetProperty("success").GetBoolean());
        Assert.Equal(1, results[1].GetProperty("queryIndex").GetInt32());
        Assert.True(results[2].GetProperty("success").GetBoolean());
    }

    private static string Invoke(Func<QueryTools, string> action)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"dbmcp-query-{Guid.NewGuid():N}.db");
        var configPath = WriteSqliteConfigFile(databasePath);
        var originalConfigPath = Environment.GetEnvironmentVariable("DB_CONFIG_PATH");

        try
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", configPath);

            var builder = Host.CreateApplicationBuilder();
            builder.Services.AddDatabaseMcpApplicationServices();
            builder.Services.AddDatabaseMcpToolServices();

            using var host = builder.Build();
            return action(host.Services.GetRequiredService<QueryTools>());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", originalConfigPath);
            DeleteFileIfExists(configPath);
            DeleteFileIfExists(databasePath);
        }
    }

    private static string WriteSqliteConfigFile(string databasePath)
    {
        var escapedDatabasePath = databasePath.Replace("\\", "\\\\", StringComparison.Ordinal);
        var configPath = Path.Combine(Path.GetTempPath(), $"dbmcp-query-config-{Guid.NewGuid():N}.json");
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
