using System.Text.Json;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using DatabaseMcpServer.Tools.Command;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DatabaseMcpServer.Tests;

public class AtomicCommandTests
{
    [Fact]
    public async Task AtomicBatch_RespectsDangerousOperationGuardBeforeAnyWrite()
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        var path = Path.Combine(fixture.Directory.FullName, "guard.db");
        Assert.True(fixture.Service.UpdateDatabase("primary", new() { ApplyConnectionString = true, ConnectionString = $"Data Source={path};Pooling=False" }).Success);
        using (var seed = fixture.Sessions.AcquireClient()) seed.Client.Ado.ExecuteCommand("create table items(v int); insert into items values(1)");
        using var result = JsonDocument.Parse(await fixture.Host.Services.GetRequiredService<CommandTools>().BatchExecuteCommands(
            [new() { Sql = "insert into items values(2)" }, new() { Sql = "delete from items" }], atomic: true));
        Assert.Equal("not-started", result.RootElement.GetProperty("transactionOutcome").GetString());
        Assert.Equal(1003, result.RootElement.GetProperty("errorCode").GetInt32());
        using var verify = fixture.Sessions.AcquireClient();
        Assert.Equal(1, verify.Client.Ado.GetInt("select count(*) from items"));
        Assert.Equal(1, verify.Client.Ado.GetInt("select v from items"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CompletionFailure_ReportsUnknownAndPreservesActualDatabaseOutcome(bool lostCommitReply)
    {
        var logger = new CleanupLogger();
        using var fixture = new RuntimeConsistencyTests.Fixture(configureServices: services =>
        {
            services.AddSingleton<AtomicTransactionCompletion>(new FaultedCompletion(lostCommitReply));
            services.AddSingleton<ILogger<AtomicCommandService>>(logger);
        });
        var path = Path.Combine(fixture.Directory.FullName, "completion.db");
        Assert.True(fixture.Service.UpdateDatabase("primary", new() { ApplyConnectionString = true, ConnectionString = $"Data Source={path};Pooling=False" }).Success);
        using (var seed = fixture.Sessions.AcquireClient()) seed.Client.Ado.ExecuteCommand("create table items(v int primary key)");
        DatabaseCommand[] commands = lostCommitReply
            ? [new() { Sql = "insert into items values(1)" }]
            : [new() { Sql = "insert into items values(1)" }, new() { Sql = "insert into items values(1)" }];
        using var result = JsonDocument.Parse(await fixture.Host.Services.GetRequiredService<CommandTools>().BatchExecuteCommands(commands, atomic: true));
        Assert.False(result.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("unknown", result.RootElement.GetProperty("transactionOutcome").GetString());
        Assert.Equal(0, result.RootElement.GetProperty("committedAffectedRows").GetInt32());
        Assert.All(result.RootElement.GetProperty("results").EnumerateArray(), item =>
            Assert.Equal("unknown", item.GetProperty("outcome").GetString()));
        using var verify = fixture.Sessions.AcquireClient();
        // A lost reply can mean the data committed. Failed rollback is cleaned by transaction disposal.
        Assert.Equal(lostCommitReply ? 1 : 0, verify.Client.Ado.GetInt("select count(*) from items"));
        var warning = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("rollback", warning.Message);
        Assert.Contains(typeof(IOException).FullName!, warning.Message);
        Assert.Contains("unknown", warning.Message);
        Assert.DoesNotContain("do-not-log", warning.Message);
        Assert.Null(warning.Exception);
    }

    private sealed class FaultedCompletion(bool lostCommitReply) : DatabaseMcpServer.Services.AtomicTransactionCompletion
    {
        public override async Task CommitAsync(System.Data.Common.DbTransaction transaction, CancellationToken token)
        {
            await base.CommitAsync(transaction, token);
            if (lostCommitReply) throw new IOException("Simulated loss after database commit");
        }

        public override Task RollbackAsync(System.Data.Common.DbTransaction transaction, CancellationToken token)
            => throw new IOException("Simulated rollback failure; Password=do-not-log");
    }

    private sealed class CleanupLogger : ILogger<AtomicCommandService>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception), exception));
    }

    [Fact]
    public async Task AtomicBatch_CommitsAllOrRollsBackAllWhileLegacyStillPartiallyCommits()
    {
        using var fixture = DatabaseRoleTests.Create();
        var tool = fixture.Host.Services.GetRequiredService<CommandTools>();
        var committed = DatabaseRoleTests.Success(await tool.BatchExecuteCommands(
            [new() { Sql = "insert into markers values (10)" }, new() { Sql = "insert into markers values (@v)", Parameters = [new() { Name = "v", Value = JsonSerializer.SerializeToElement(11) }] }], atomic: true));
        Assert.Equal("committed", committed.GetProperty("transactionOutcome").GetString());
        Assert.Equal(2, committed.GetProperty("committedAffectedRows").GetInt32());
        using var failed = JsonDocument.Parse(await tool.BatchExecuteCommands(
            [new() { Sql = "insert into markers values (20)" }, new() { Sql = "insert into markers values (1)" }, new() { Sql = "insert into markers values (21)" }], atomic: true));
        Assert.False(failed.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("rolled-back", failed.RootElement.GetProperty("transactionOutcome").GetString());
        Assert.Equal("not-executed", failed.RootElement.GetProperty("results")[2].GetProperty("outcome").GetString());
        Assert.Equal(0, failed.RootElement.GetProperty("committedAffectedRows").GetInt32());
        using (var verify = fixture.Sessions.AcquireClient()) Assert.Equal(0, verify.Client.Ado.GetInt("select count(*) from markers where value>=20"));
        DatabaseRoleTests.Success(await tool.BatchExecuteCommands([new() { Sql = "insert into markers values(20)" }, new() { Sql = "insert into markers values(1)" }]));
        using var final = fixture.Sessions.AcquireClient();
        Assert.Equal(1, final.Client.Ado.GetInt("select count(*) from markers where value=20"));
    }

    [Theory]
    [InlineData("create table surprise(v int)")]
    [InlineData("insert into markers values(2); delete from markers")]
    [InlineData("COMMIT")]
    [InlineData("insert into markers values(4) insert into markers values(5)")]
    [InlineData("update markers set value=3 WAITFOR DELAY '00:01:00'")]
    public async Task AtomicBatch_ValidatesEntireBatchBeforeAnyWrite(string invalid)
    {
        using var fixture = DatabaseRoleTests.Create();
        var tool = fixture.Host.Services.GetRequiredService<CommandTools>();
        using var result = JsonDocument.Parse(await tool.BatchExecuteCommands([new() { Sql = "insert into markers values(2)" }, new() { Sql = invalid }], atomic: true));
        Assert.Equal("not-started", result.RootElement.GetProperty("transactionOutcome").GetString());
        using var verify = fixture.Sessions.AcquireClient();
        Assert.Equal(1, verify.Client.Ado.GetInt("select count(*) from markers"));
    }

    [Fact]
    public async Task AtomicBatch_AllowsCaseExpressions()
    {
        using var fixture = DatabaseRoleTests.Create();
        var result = DatabaseRoleTests.Success(await fixture.Host.Services.GetRequiredService<CommandTools>().BatchExecuteCommands(
            [new() { Sql = "update markers set value=case when value=1 then 8 else value end" }], atomic: true));
        Assert.Equal("committed", result.GetProperty("transactionOutcome").GetString());
        using var verify = fixture.Sessions.AcquireClient();
        Assert.Equal(8, verify.Client.Ado.GetInt("select value from markers"));
    }

    [Fact]
    public async Task AtomicCancellation_ReturnsRollbackOutcomeAndReleasesConnection()
    {
        using var cancel = new CancellationTokenSource();
        using var fixture = new RuntimeConsistencyTests.Fixture(client => client.Aop.OnLogExecuted = (sql, _) =>
        {
            if (sql.StartsWith("insert", StringComparison.OrdinalIgnoreCase)) cancel.Cancel();
        });
        var path = Path.Combine(fixture.Directory.FullName, "atomic.db");
        Assert.True(fixture.Service.UpdateDatabase("primary", new() { ApplyConnectionString = true, ConnectionString = $"Data Source={path};Pooling=False" }).Success);
        using (var seed = fixture.Sessions.AcquireClient()) seed.Client.Ado.ExecuteCommand("create table items(v int)");
        using var result = JsonDocument.Parse(await fixture.Host.Services.GetRequiredService<CommandTools>().BatchExecuteCommands(
            [new() { Sql = "insert into items values(1)" }, new() { Sql = "insert into items values(2)" }], cancellationToken: cancel.Token, atomic: true));
        Assert.True(result.RootElement.GetProperty("cancelled").GetBoolean());
        Assert.Equal("rolled-back", result.RootElement.GetProperty("transactionOutcome").GetString());
        Assert.False(result.RootElement.GetProperty("results")[1].GetProperty("executed").GetBoolean());
        using var verify = fixture.Sessions.AcquireClient();
        Assert.Equal(0, verify.Client.Ado.GetInt("select count(*) from items"));
    }
}
