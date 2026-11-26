using System.Diagnostics;
using System.Text.Json;
using DatabaseMcpServer.Cli;

namespace DatabaseMcpServer.Tests;

public class CursorProcessTests
{
    [Theory]
    [InlineData("query_table_page")]
    [InlineData("list_objects")]
    public async Task Cursors_ContinueAcrossProcessesAndRejectChangedBindings(string tool)
    {
        using var fixture = DatabaseRoleTests.Create();
        using (var setup = fixture.Sessions.AcquireClient())
            setup.Client.Ado.ExecuteCommand("INSERT INTO markers VALUES(3); CREATE TABLE cursor_a(id INTEGER PRIMARY KEY); CREATE TABLE cursor_b(id INTEGER PRIMARY KEY)");
        var options = tool == "query_table_page"
            ? new[] { "--table-name", "markers", "--page-size", "1" }
            : new[] { "--kind", "table", "--name-prefix", "cursor_", "--limit", "1" };
        var first = await InvokeAsync(fixture, fixture.Path, tool, options);
        Assert.Equal(0, first.ExitCode);
        Assert.True(first.Result.GetProperty("success").GetBoolean());
        var cursor = first.Result.GetProperty("nextCursor").GetString();
        Assert.False(string.IsNullOrEmpty(cursor));
        var continuation = options.Concat(new[] { "--cursor", cursor! }).ToArray();
        var second = await InvokeAsync(fixture, fixture.Path, tool, continuation);
        Assert.Equal(0, second.ExitCode);
        Assert.True(second.Result.GetProperty("success").GetBoolean(), second.Result.ToString());
        if (tool == "query_table_page")
            Assert.Equal(3, Assert.Single(second.Result.GetProperty("data").EnumerateArray()).GetProperty("value").GetInt32());
        else
            Assert.Equal("cursor_b", Assert.Single(second.Result.GetProperty("objects").EnumerateArray()).GetProperty("name").GetString());

        var otherDatabase = await InvokeAsync(fixture, fixture.Path, tool, continuation.Concat(new[] { "--database-name", "analytics" }).ToArray());
        AssertInvalid(otherDatabase);
        var changedQuery = continuation.ToArray();
        changedQuery[3] = tool == "query_table_page" ? "2" : "cursor_b";
        AssertInvalid(await InvokeAsync(fixture, fixture.Path, tool, changedQuery));
        var copiedConfig = Path.Combine(fixture.Directory.FullName, "copied.json");
        File.Copy(fixture.Path, copiedConfig);
        AssertInvalid(await InvokeAsync(fixture, copiedConfig, tool, continuation));
        var otherDirectory = Directory.CreateDirectory(Path.Combine(fixture.Directory.FullName, "other")).FullName;
        AssertInvalid(await InvokeAsync(fixture, fixture.Path, tool, continuation, otherDirectory));
        var originalConfig = await File.ReadAllTextAsync(fixture.Path);
        await File.AppendAllTextAsync(fixture.Path, "\n ");
        AssertInvalid(await InvokeAsync(fixture, fixture.Path, tool, continuation));
        await File.WriteAllTextAsync(fixture.Path, originalConfig);
        var restored = await InvokeAsync(fixture, fixture.Path, tool, continuation);
        Assert.Equal(0, restored.ExitCode);
        Assert.Equal(second.Result.GetProperty(tool == "query_table_page" ? "data" : "objects").GetRawText(),
            restored.Result.GetProperty(tool == "query_table_page" ? "data" : "objects").GetRawText());
    }

    private static void AssertInvalid((int ExitCode, JsonElement Result) response)
    {
        Assert.Equal(1, response.ExitCode);
        Assert.False(response.Result.GetProperty("success").GetBoolean());
        Assert.Equal(1004, response.Result.GetProperty("errorCode").GetInt32());
    }

    private static async Task<(int ExitCode, JsonElement Result)> InvokeAsync(
        RuntimeConsistencyTests.Fixture fixture, string config, string tool, string[] arguments, string? workingDirectory = null)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory ?? fixture.Directory.FullName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(typeof(CliRunner).Assembly.Location);
        foreach (var argument in new[] { "tool", tool, "--config", config }.Concat(arguments)) start.ArgumentList.Add(argument);
        // Keep child processes independent of the developer's logging and execution policy.
        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("DB_", StringComparison.Ordinal)
                     || k.StartsWith("SEQ_", StringComparison.Ordinal)).ToArray()) start.Environment.Remove(key);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(deadline.Token);
            var output = await stdout;
            Assert.False(string.IsNullOrWhiteSpace(output), await stderr);
            using var json = JsonDocument.Parse(output);
            return (process.ExitCode, json.RootElement.Clone());
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
