using System.Text.Json;
using DatabaseMcpServer.Cli;

namespace DatabaseMcpServer.Tests;

public class CliRunnerTests
{
    [Fact]
    public async Task RunAsync_ShouldListTools_WithoutConfig()
    {
        var runner = new CliRunner();
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = await runner.RunAsync(["list"], stdout, stderr);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, stdout.ToString());
        Assert.Contains("list_databases", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ShouldRejectProtectedTool_WithoutYesBeforeConfigResolution()
    {
        var runner = new CliRunner();
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = await runner.RunAsync(["drop_table", "--table-name", "users"], stdout, stderr);

        Assert.Equal(2, exitCode);
        Assert.Contains("--yes", stderr.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, stdout.ToString());
    }

    [Fact]
    public async Task RunAsync_ShouldExecuteListDatabases_WithExplicitConfig()
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

        try
        {
            var runner = new CliRunner();
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exitCode = await runner.RunAsync(["list_databases", "--config", configPath], stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr.ToString());

            using var document = JsonDocument.Parse(stdout.ToString());
            Assert.True(document.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal(2, document.RootElement.GetProperty("totalDatabases").GetInt32());
            Assert.Equal("primary", document.RootElement.GetProperty("currentDatabase").GetString());
        }
        finally
        {
            DeleteFileIfExists(configPath);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldExecuteGetDatabaseConfig_WithExplicitConfig()
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

        try
        {
            var runner = new CliRunner();
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exitCode = await runner.RunAsync(["get_database_config", "--config", configPath], stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr.ToString());

            using var document = JsonDocument.Parse(stdout.ToString());
            Assert.True(document.RootElement.GetProperty("configured").GetBoolean());
            Assert.Equal("primary", document.RootElement.GetProperty("currentDatabase").GetString());
            Assert.Contains("Password=****", document.RootElement.GetProperty("connectionString").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            DeleteFileIfExists(configPath);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldExecuteSwitchDatabase_WithExplicitConfig()
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

        try
        {
            var runner = new CliRunner();
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exitCode = await runner.RunAsync(
                ["switch_database", "--database-name", "analytics", "--config", configPath],
                stdout,
                stderr);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr.ToString());

            using var document = JsonDocument.Parse(stdout.ToString());
            Assert.True(document.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal("primary", document.RootElement.GetProperty("previousDatabase").GetString());
            Assert.Equal("analytics", document.RootElement.GetProperty("currentDatabase").GetString());
        }
        finally
        {
            DeleteFileIfExists(configPath);
        }
    }

    private static string WriteConfigFile(string json)
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"dbmcp-cli-{Guid.NewGuid():N}.json");
        File.WriteAllText(configPath, json);
        return configPath;
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
