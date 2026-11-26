using System.Text.Json;

namespace DatabaseMcpServer.Tests.Integration;

[Trait("Category", "Integration")]
public class AtomicFailureIntegrationTests
{
    [ProviderTheory]
    [InlineData("SqlServer")]
    [InlineData("PostgreSQL")]
    public async Task CancellationDuringBlockedWrite_RollsBackEarlierWrite(string provider)
    {
        using var fixture = new ProviderFixture(provider);
        using var observer = new ProviderFixture(provider);
        var name = "atomic_" + Guid.NewGuid().ToString("N");
        fixture.Observer.Ado.ExecuteCommand($"CREATE TABLE {name}(id int primary key,v int)");
        fixture.Observer.Ado.ExecuteCommand($"CREATE TABLE log_{name}(id int primary key)");
        fixture.Observer.Ado.ExecuteCommand($"INSERT INTO {name} VALUES(1,0)");
        try
        {
            fixture.Observer.Ado.BeginTran();
            fixture.Observer.Ado.ExecuteCommand($"UPDATE {name} SET v=1 WHERE id=1");
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var marker = "UPDATE " + name;
            var pending = fixture.Command.BatchExecuteCommands([
                new() { Sql = $"INSERT INTO log_{name} VALUES(2)" },
                new() { Sql = $"UPDATE {name} SET v=2 WHERE id=1" }],
                atomic: true, cancellationToken: cancellation.Token);
            try
            {
                if (provider == "SqlServer")
                {
                    var blocker = fixture.Observer.Ado.GetInt("SELECT @@SPID");
                    using var observed = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    while (observer.Observer.Ado.GetInt("SELECT COUNT(*) FROM sys.dm_exec_requests WHERE blocking_session_id=@id",
                        new SqlSugar.SugarParameter("@id", blocker)) == 0)
                        await Task.Delay(50, observed.Token);
                }
                else await observer.WaitForActivity(marker, true);
            }
            catch
            {
                var activity = provider == "SqlServer" ? observer.Observer.Ado.SqlQuery<dynamic>("SELECT session_id,blocking_session_id,wait_type,status,command FROM sys.dm_exec_requests WHERE session_id>50") : null;
                cancellation.Cancel();
                var failure = await pending.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Fail("Blocked activity was not observed: " + JsonSerializer.Serialize(activity) + ". Atomic response: " + failure);
                throw;
            }
            cancellation.Cancel();
            using var result = JsonDocument.Parse(await pending.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.True(result.RootElement.GetProperty("cancelled").GetBoolean());
            Assert.Equal("rolled-back", result.RootElement.GetProperty("transactionOutcome").GetString());
            fixture.Observer.Ado.RollbackTran();
            Assert.Equal(0, fixture.Observer.Ado.GetInt($"SELECT COUNT(*) FROM log_{name}"));
            await fixture.AssertUsable();
        }
        finally
        {
            if (fixture.Observer.Ado.Transaction != null) fixture.Observer.Ado.RollbackTran();
            fixture.Observer.Ado.ExecuteCommand($"DROP TABLE {name}");
            fixture.Observer.Ado.ExecuteCommand($"DROP TABLE log_{name}");
        }
    }

    [ProviderTheory]
    [InlineData("SqlServer")]
    [InlineData("PostgreSQL")]
    public async Task Deadlock_DoesNotReplayOrPartiallyCommitVictim(string provider)
    {
        using var barrier = new Barrier(2);
        void Observe(SqlSugar.ISqlSugarClient client) => client.Aop.OnLogExecuted = (sql, _) =>
        {
            if (sql.Contains("/*first*/", StringComparison.Ordinal) && !barrier.SignalAndWait(TimeSpan.FromSeconds(15)))
                throw new TimeoutException("Transactions did not reach the deadlock barrier");
        };
        using var first = new ProviderFixture(provider, Observe);
        using var second = new ProviderFixture(provider, Observe);
        var name = "deadlock_" + Guid.NewGuid().ToString("N");
        first.Observer.Ado.ExecuteCommand($"CREATE TABLE {name}(id int primary key,v int)");
        first.Observer.Ado.ExecuteCommand($"INSERT INTO {name} VALUES(1,0),(2,0)");
        try
        {
            var a = Task.Run(() => first.Command.BatchExecuteCommands([
                new() { Sql = $"UPDATE {name} SET v=v+1 WHERE id=1 /*first*/" },
                new() { Sql = $"UPDATE {name} SET v=v+1 WHERE id=2" }], atomic: true));
            var b = Task.Run(() => second.Command.BatchExecuteCommands([
                new() { Sql = $"UPDATE {name} SET v=v+1 WHERE id=2 /*first*/" },
                new() { Sql = $"UPDATE {name} SET v=v+1 WHERE id=1" }], atomic: true));
            var results = await Task.WhenAll(a, b).WaitAsync(TimeSpan.FromSeconds(40));
            var outcomes = results.Select(payload =>
            {
                using var result = JsonDocument.Parse(payload);
                return result.RootElement.GetProperty("transactionOutcome").GetString();
            }).ToArray();
            Assert.Single(outcomes, outcome => outcome == "committed");
            Assert.Single(outcomes, outcome => outcome is "rolled-back" or "unknown");
            Assert.Equal(2, first.Observer.Ado.GetInt($"SELECT COUNT(*) FROM {name} WHERE v=1"));
            await first.AssertUsable();
            await second.AssertUsable();
        }
        finally { first.Observer.Ado.ExecuteCommand($"DROP TABLE {name}"); }
    }
}
