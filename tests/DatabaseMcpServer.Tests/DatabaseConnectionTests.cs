using System.Text.Json;
using DatabaseMcpServer.Cli;
using DatabaseMcpServer.Models;

namespace DatabaseMcpServer.Tests;

public class DatabaseConnectionTests
{
    [Theory]
    [InlineData("{}", false)]
    [InlineData("{\"allowDangerousOperations\":true}", true)]
    [InlineData("{\"allowDangerousOperations\":false}", false)]
    [InlineData("{\"allowDangerousOperations\":true,\"enableDangerousOperations\":false}", false)]
    [InlineData("{\"enableDangerousOperations\":false,\"allowDangerousOperations\":true}", false)]
    [InlineData("{\"allowDangerousOperations\":false,\"enableDangerousOperations\":true}", true)]
    [InlineData("{\"enableDangerousOperations\":true,\"allowDangerousOperations\":false}", true)]
    public void Deserialize_ShouldPreferNewDangerousOperationsField(string json, bool expected)
    {
        var connection = JsonSerializer.Deserialize<DatabaseConnection>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(connection);
        Assert.Equal(expected, connection.EnableDangerousOperations);
    }

    [Fact]
    public void SaveLegacyConfig_ShouldPreserveOptInAndWriteOnlyNewField()
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"dbmcp-legacy-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(configPath, """
                {
                  "databases": [{
                    "name": "maintenance",
                    "dbType": "Sqlite",
                    "connectionString": "Data Source=:memory:",
                    "allowDangerousOperations": true
                  }]
                }
                """);
            var fileService = new CliConfigFileService();
            var config = fileService.Load(configPath);
            config.Databases[0].Description = "Edited description";

            fileService.Save(configPath, config);

            using var saved = JsonDocument.Parse(File.ReadAllText(configPath));
            var connection = saved.RootElement.GetProperty("databases")[0];
            Assert.False(connection.TryGetProperty("allowDangerousOperations", out _));
            Assert.False(connection.TryGetProperty("legacyAllowDangerousOperations", out _));
            Assert.True(connection.GetProperty("enableDangerousOperations").GetBoolean());
            Assert.True(fileService.Load(configPath).Databases[0].EnableDangerousOperations);
        }
        finally
        {
            File.Delete(configPath);
        }
    }
}
