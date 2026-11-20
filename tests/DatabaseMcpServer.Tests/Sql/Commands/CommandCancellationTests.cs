using DatabaseMcpServer.Models;
using System.Text.Json;
using DatabaseMcpServer.Tools.Command;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseMcpServer.Tests;

public class CommandCancellationTests
{
    [Fact]
    public async Task BatchCancellation_PreservesCompletedWriteAndStopsFollowingCommands()
    {
        using var cancel = new CancellationTokenSource();
        using var fixture = new RuntimeConsistencyTests.Fixture(client => client.Aop.OnLogExecuted = (sql, _) =>
        {
            if (sql.StartsWith("insert", StringComparison.OrdinalIgnoreCase)) cancel.Cancel();
        });
        var path = Path.Combine(fixture.Directory.FullName, "commands.db");
        Assert.True(fixture.Service.UpdateDatabase("primary", new()
            { ApplyConnectionString = true, ConnectionString = $"Data Source={path};Pooling=False" }).Success);
        using (var seed = fixture.Sessions.AcquireClient())
        {
            seed.Client.Ado.ExecuteCommand("create table items(value integer)");
        }
        DatabaseCommand[] commands = [new() { Sql = "insert into items values(1)" }, new() { Sql = "insert into items values(2)" }];
        var tool = fixture.Host.Services.GetRequiredService<CommandTools>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tool.BatchExecuteCommands(commands, cancellationToken: cancel.Token));
        using var verify = fixture.Sessions.AcquireClient();
        Assert.Equal(1, verify.Client.Ado.GetInt("select count(*) from items"));
        Assert.Equal(1, verify.Client.Ado.GetInt("select value from items"));
    }

    [Fact]
    public async Task BatchSqlFailure_KeepsPerCommandResultsAndContinues()
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        Assert.True(fixture.Service.UpdateDatabase("primary", new()
            { ApplyConnectionString = true, ConnectionString = $"Data Source={Path.Combine(fixture.Directory.FullName, "batch.db")};Pooling=False" }).Success);
        using (var seed = fixture.Sessions.AcquireClient())
            seed.Client.Ado.ExecuteCommand("create table items(value integer)");
        DatabaseCommand[] commands = [new() { Sql = "insert into missing values(1)" }, new() { Sql = "insert into items values(@value)", Parameters = [new() { Name = "value", Value = JsonSerializer.SerializeToElement(23) }] }];
        var tool = fixture.Host.Services.GetRequiredService<CommandTools>();
        using var result = JsonDocument.Parse(await tool.BatchExecuteCommands(commands));
        var results = result.RootElement.GetProperty("results");
        Assert.False(results[0].GetProperty("success").GetBoolean());
        Assert.True(results[1].GetProperty("success").GetBoolean());
        Assert.Equal(1, results[1].GetProperty("affectedRows").GetInt32());
        using var verify = fixture.Sessions.AcquireClient();
        Assert.Equal(23, verify.Client.Ado.GetInt("select value from items"));
    }
}
