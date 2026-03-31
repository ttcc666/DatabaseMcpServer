using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Services;
using DatabaseMcpServer.Strategies.DBSetting;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace DatabaseMcpServer.Tests;

public class DatabaseConfigServiceTests
{
    [Fact]
    public void GetConfigurationSummary_ShouldMaskSensitiveConnectionStringParts()
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"dbmcp-{Guid.NewGuid():N}.json");
        var originalConfigPath = Environment.GetEnvironmentVariable("DB_CONFIG_PATH");

        try
        {
            File.WriteAllText(configPath, """
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

            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", configPath);

            var helper = new DatabaseHelper(NullLogger<DatabaseHelper>.Instance);
            var serializer = new JsonResultSerializer();
            IDatabaseOptimizationStrategyFactory strategyFactory = new DatabaseOptimizationStrategyFactory();
            var clientFactory = new SqlSugarClientFactory(NullLogger<SqlSugarClientFactory>.Instance, helper, strategyFactory);
            var service = new DatabaseConfigService(NullLogger<DatabaseConfigService>.Instance, helper, clientFactory, serializer);

            var summary = service.GetConfigurationSummary();

            using var document = JsonDocument.Parse(summary);
            Assert.Equal("default", document.RootElement.GetProperty("currentDatabase").GetString());
            Assert.Equal("SqlServer", document.RootElement.GetProperty("databaseType").GetString());
            Assert.Contains("Password=****", document.RootElement.GetProperty("connectionString").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", originalConfigPath);
            if (File.Exists(configPath))
            {
                File.Delete(configPath);
            }
        }
    }
}
