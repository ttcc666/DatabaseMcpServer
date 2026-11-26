using DatabaseMcpServer.Models;
using System.Text.Json;
using DatabaseMcpServer.Tools;
using DatabaseMcpServer.Tools.Command;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseMcpServer.Tests;

public class CommandCancellationTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task BatchDeadline_ReportsConfirmedWritesAndUnconfirmedCommand(bool loseAcknowledgment, bool hasTail)
    {
        const string secondWrite = "insert into items values(2)";
        using var fixture = new RuntimeConsistencyTests.Fixture(client => client.Aop.OnLogExecuted = (sql, _) =>
        {
            if (sql != secondWrite) return;
            var token = Assert.IsType<CancellationToken>(client.Ado.CancellationToken);
            Assert.True(token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)));
            if (loseAcknowledgment) token.ThrowIfCancellationRequested();
        });
        var path = Path.Combine(fixture.Directory.FullName, "batch-deadline.db");
        Assert.True(fixture.Service.UpdateDatabase("primary", new()
            { ApplyConnectionString = true, ConnectionString = $"Data Source={path};Pooling=False" }).Success);
        using (var seed = fixture.Sessions.AcquireClient())
            seed.Client.Ado.ExecuteCommand("create table items(value integer)");

        var commands = new List<DatabaseCommand> { new() { Sql = "insert into items values(1)" }, new() { Sql = secondWrite } };
        if (hasTail) commands.Add(new() { Sql = "insert into items values(3)" });
        var tool = fixture.Host.Services.GetRequiredService<CommandTools>();
        var payload = await tool.BatchExecuteCommands(commands.ToArray(), commandTimeoutSeconds: 1).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(ToolInvoker.IsToolFailure(payload));
        using var document = JsonDocument.Parse(payload);
        var response = document.RootElement;
        Assert.Equal(1011, response.GetProperty("errorCode").GetInt32());
        Assert.False(response.GetProperty("retryable").GetBoolean());
        var progress = response.GetProperty("data");
        Assert.Equal(commands.Count, progress.GetProperty("totalCommands").GetInt32());
        var firstUnsent = progress.GetProperty("notExecutedFromIndex");
        if (hasTail) Assert.Equal(2, firstUnsent.GetInt32());
        else Assert.Equal(JsonValueKind.Null, firstUnsent.ValueKind);
        var unknown = progress.GetProperty("unknownCommandIndex");
        if (loseAcknowledgment)
        {
            Assert.Equal(1, unknown.GetInt32());
            Assert.Contains("核对", response.GetProperty("hint").GetString());
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, unknown.ValueKind);
        }
        var results = progress.GetProperty("results").EnumerateArray().ToArray();
        Assert.Equal(loseAcknowledgment ? 1 : 2, results.Length);
        for (var index = 0; index < results.Length; index++)
        {
            Assert.Equal(index, results[index].GetProperty("commandIndex").GetInt32());
            Assert.True(results[index].GetProperty("success").GetBoolean());
            Assert.Equal(1, results[index].GetProperty("affectedRows").GetInt32());
        }

        using var verify = await fixture.Sessions.AcquireClientAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(new[] { 1, 2 }, verify.Client.Ado.SqlQuery<int>("select value from items order by value"));
    }

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
