using System.Diagnostics;
using System.Text.Json;
using DatabaseMcpServer.Cli;
using DatabaseMcpServer.Configuration;

namespace DatabaseMcpServer.Tests;

public class CliConfigurationTests
{
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public async Task Export_UsesCliConfigurationPrecedence(bool explicitConfig, int expectedValue)
    {
        using var fixture = DatabaseRoleTests.Create();
        var environmentConfig = Path.Combine(fixture.Directory.FullName, "environment.json");
        var config = new ConfigurationFileStore().Load(fixture.Path);
        foreach (var connection in config.Databases)
            connection.IsDefault = connection.Name == "analytics";
        await File.WriteAllTextAsync(environmentConfig, JsonSerializer.Serialize(config));
        var outputPath = Path.Combine(fixture.Directory.FullName, "export.jsonl");
        var arguments = new List<string> { "export", "--sql", "select value from markers", "--output", outputPath };
        if (explicitConfig) arguments.AddRange(["--config", environmentConfig]);

        var result = await InvokeAsync(fixture.Directory.FullName, arguments, new() { ["DB_CONFIG_PATH"] = environmentConfig });

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Stderr);
        using var summary = JsonDocument.Parse(result.Stdout);
        Assert.True(summary.RootElement.GetProperty("success").GetBoolean());
        using var exported = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
        Assert.Equal(expectedValue, exported.RootElement.GetProperty("value").GetInt32());
    }

    [Theory]
    [InlineData("databases.json")]
    [InlineData("local-databases.json")]
    public async Task ToolList_UsesCurrentDirectoryBeforeEnvironment(string fileName)
    {
        using var directory = new TemporaryUserProfile();
        await File.WriteAllTextAsync(Path.Combine(directory.DirectoryPath, fileName),
            """{"databases":[{"name":"local","dbType":"Sqlite","connectionString":"Data Source=:memory:","isDefault":true}]}""");

        var result = await InvokeAsync(directory.DirectoryPath, ["tool", "list", "--json"],
            new() { ["DB_CONFIG_PATH"] = Path.Combine(directory.DirectoryPath, "missing.json") });

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Stderr);
        using var response = JsonDocument.Parse(result.Stdout);
        Assert.True(response.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(response.RootElement.GetProperty("tools").EnumerateArray(),
            tool => tool.GetProperty("name").GetString() == "query_database");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ToolList_AllIgnoresRuntimeConfiguration(bool json)
    {
        using var directory = new TemporaryUserProfile();
        var arguments = new List<string> { "tool", "list", "--all", "--config", "missing.json" };
        if (json) arguments.Add("--json");

        var result = await InvokeAsync(directory.DirectoryPath, arguments,
            new() { ["DB_MAX_CONCURRENT"] = "invalid", ["DB_MCP_TOOL_PROFILE"] = "invalid" });

        Assert.Equal(0, result.ExitCode);
        if (json)
        {
            Assert.Equal(string.Empty, result.Stderr);
            using var response = JsonDocument.Parse(result.Stdout);
            Assert.True(response.RootElement.GetProperty("success").GetBoolean());
            var tools = response.RootElement.GetProperty("tools").EnumerateArray()
                .Select(tool => tool.GetProperty("name").GetString()).ToArray();
            Assert.Contains("query_database", tools);
            Assert.Contains("drop_table", tools);
            Assert.Contains("mongo_find", tools);
            Assert.NotEqual(JsonValueKind.Null, response.RootElement.GetProperty("capabilityCatalog").ValueKind);
        }
        else
        {
            Assert.Equal(string.Empty, result.Stdout);
            Assert.Contains("query_database", result.Stderr);
            Assert.Contains("drop_table", result.Stderr);
            Assert.Contains("mongo_find", result.Stderr);
        }
    }

    [Theory]
    [InlineData("list")]
    [InlineData("export")]
    public async Task DynamicCommands_RejectMissingExplicitConfig(string command)
    {
        using var directory = new TemporaryUserProfile();
        var missing = Path.Combine(directory.DirectoryPath, "missing.json");
        var output = Path.Combine(directory.DirectoryPath, "export.jsonl");
        string[] arguments = command == "list"
            ? ["tool", "list", "--json", "--config", missing]
            : ["export", "--sql", "select 1", "--output", output, "--config", missing];

        var result = await InvokeAsync(directory.DirectoryPath, arguments, new());

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Contains(missing, result.Stderr);
        Assert.False(File.Exists(output));
        Assert.Empty(Directory.EnumerateFiles(directory.DirectoryPath, ".dbmcp-export-*.tmp"));
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> InvokeAsync(
        string workingDirectory, IEnumerable<string> arguments, Dictionary<string, string> environment)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(typeof(CliRunner).Assembly.Location);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("DB_", StringComparison.Ordinal)
                     || key.StartsWith("SEQ_", StringComparison.Ordinal) || key == "ENABLE_MONITOR_CONFIG").ToArray())
            start.Environment.Remove(key);
        foreach (var setting in environment) start.Environment[setting.Key] = setting.Value;
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(deadline.Token);
            return (process.ExitCode, await stdout, await stderr);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }
}
