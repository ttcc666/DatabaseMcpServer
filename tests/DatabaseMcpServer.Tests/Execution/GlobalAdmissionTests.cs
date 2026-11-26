using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseMcpServer.Tests;

public class GlobalAdmissionTests
{
    [Fact]
    public async Task EnvironmentAndCapabilityReport_ExposeTotalAdmissionPolicy()
    {
        var previousActive = Environment.GetEnvironmentVariable("DB_MAX_TOTAL_CONCURRENT");
        var previousQueued = Environment.GetEnvironmentVariable("DB_MAX_TOTAL_QUEUED");
        try
        {
            Environment.SetEnvironmentVariable("DB_MAX_TOTAL_CONCURRENT", "3");
            Environment.SetEnvironmentVariable("DB_MAX_TOTAL_QUEUED", "0");
            using var fixture = new RuntimeConsistencyTests.Fixture();
            var report = DatabaseRoleTests.Success(await fixture.Host.Services.GetRequiredService<DatabaseMcpServer.Tools.Management.ConnectionTools>().GetDatabaseCapabilities());
            Assert.Equal(3, report.GetProperty("budgets").GetProperty("maxTotalConcurrent").GetInt32());
            Assert.Equal(0, report.GetProperty("budgets").GetProperty("maxTotalQueued").GetInt32());
            var leases = Enumerable.Range(0, 3).Select(_ => fixture.Sessions.AcquireClient()).ToArray();
            try
            {
                var error = await Assert.ThrowsAsync<DatabaseMcpException>(() => fixture.Sessions.AcquireClientAsync("analytics").AsTask());
                Assert.Equal(DatabaseErrorCode.DatabaseBusy, error.ErrorCode);
            }
            finally { foreach (var lease in leases) lease.Dispose(); }
            Environment.SetEnvironmentVariable("DB_MAX_TOTAL_CONCURRENT", "0");
            Assert.Throws<InvalidOperationException>(() => DatabaseExecutionOptions.FromEnvironment());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DB_MAX_TOTAL_CONCURRENT", previousActive);
            Environment.SetEnvironmentVariable("DB_MAX_TOTAL_QUEUED", previousQueued);
        }
    }

    [Fact]
    public async Task TotalLimit_SkipsHotConnectionAndPreservesConnectionOrder()
    {
        using var coordinator = new DatabaseExecutionCoordinator(new(1, 3, 10) { MaxTotalConcurrent = 2, MaxTotalQueued = 4 });
        using var first = coordinator.Reserve(Connection("a")).Acquire();
        using var second = coordinator.Reserve(Connection("b")).Acquire();
        var a1 = coordinator.Reserve(Connection("a")).AcquireAsync().AsTask();
        var a2 = coordinator.Reserve(Connection("a")).AcquireAsync().AsTask();
        var c = coordinator.Reserve(Connection("c")).AcquireAsync().AsTask();
        Assert.False(a1.IsCompleted);
        Assert.False(a2.IsCompleted);
        Assert.False(c.IsCompleted);
        second.Dispose();
        using var cLease = await c.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("c", cLease.Context.DatabaseName);
        Assert.False(a1.IsCompleted);
        first.Dispose();
        using var a1Lease = await a1.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(a2.IsCompleted);
        var a3 = coordinator.Reserve(Connection("a")).AcquireAsync().AsTask();
        a1Lease.Dispose();
        using var a2Lease = await a2.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(a3.IsCompleted);
        a2Lease.Dispose();
        using var a3Lease = await a3.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("a", a3Lease.Context.DatabaseName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task TotalQueue_RejectsOverflowAndCancellationReturnsCapacity(int queueLimit)
    {
        using var coordinator = new DatabaseExecutionCoordinator(new(3, 4, 10) { MaxTotalConcurrent = 1, MaxTotalQueued = queueLimit });
        using var first = coordinator.Reserve(Connection("a")).Acquire();
        using var cancelled = new CancellationTokenSource();
        var pending = Enumerable.Range(0, queueLimit).Select(i => coordinator.Reserve(Connection("waiting" + i)).AcquireAsync(cancelled.Token).AsTask()).ToArray();
        Assert.All(pending, task => Assert.False(task.IsCompleted));
        var overflow = await Assert.ThrowsAsync<DatabaseMcpException>(() => coordinator.Reserve(Connection("overflow")).AcquireAsync().AsTask());
        Assert.Equal(DatabaseErrorCode.DatabaseBusy, overflow.ErrorCode);
        Assert.Contains("总等待队列", overflow.Message);
        cancelled.Cancel();
        foreach (var task in pending) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        first.Dispose();
        using var next = await coordinator.Reserve(Connection("next")).AcquireAsync();
        Assert.Equal("next", next.Context.DatabaseName);
    }

    [Fact]
    public async Task ConnectionQueue_RejectsOnlyHotConnectionWhileGlobalRoomRemains()
    {
        using var coordinator = new DatabaseExecutionCoordinator(new(1, 1, 10) { MaxTotalConcurrent = 2, MaxTotalQueued = 4 });
        using var first = coordinator.Reserve(Connection("a")).Acquire();
        var pending = coordinator.Reserve(Connection("a")).AcquireAsync().AsTask();
        var overflow = await Assert.ThrowsAsync<DatabaseMcpException>(() => coordinator.Reserve(Connection("a")).AcquireAsync().AsTask());
        Assert.Equal(DatabaseErrorCode.DatabaseBusy, overflow.ErrorCode);
        Assert.Contains("连接等待队列", overflow.Message);
        using var other = coordinator.Reserve(Connection("b")).Acquire();
        Assert.False(pending.IsCompleted);
        first.Dispose();
        using var granted = await pending.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task TotalQueueTimeout_AndSynchronousWaitDoNotLeakSlots()
    {
        using var metrics = new AdmissionMetrics();
        using var coordinator = new DatabaseExecutionCoordinator(new(2, 2, 1) { MaxTotalConcurrent = 1 });
        using var first = coordinator.Reserve(Connection("a")).Acquire();
        var expired = await Assert.ThrowsAsync<DatabaseMcpException>(() => coordinator.Reserve(Connection("b")).AcquireAsync().AsTask());
        Assert.Equal(DatabaseErrorCode.DatabaseBusy, expired.ErrorCode);
        Assert.Contains("等待超时", expired.Message);
        var enteredQueue = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        metrics.OnQueued = () => enteredQueue.TrySetResult();
        var synchronous = Task.Run(() => coordinator.Reserve(Connection("c")).Acquire());
        await enteredQueue.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(synchronous.IsCompleted);
        first.Dispose();
        using (var lease = await synchronous.WaitAsync(TimeSpan.FromSeconds(5))) Assert.Equal("c", lease.Context.DatabaseName);
        Assert.Equal(0, metrics.Active);
        Assert.Equal(0, metrics.Queued);
        Assert.Equal(1, metrics.PeakActive);
    }

    [Fact]
    public async Task CancellationReleaseRace_AndShutdownReturnEverySlotOnce()
    {
        using var metrics = new AdmissionMetrics();
        using var coordinator = new DatabaseExecutionCoordinator(new(1, 2, 10) { MaxTotalConcurrent = 1 });
        for (var i = 0; i < 50; i++)
        {
            using var first = coordinator.Reserve(Connection("a")).Acquire();
            using var cancellation = new CancellationTokenSource();
            var waiting = coordinator.Reserve(Connection("b")).AcquireAsync(cancellation.Token).AsTask();
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancel = Task.Run(async () => { await start.Task; cancellation.Cancel(); });
            var release = Task.Run(async () => { await start.Task; first.Dispose(); });
            start.SetResult();
            await Task.WhenAll(cancel, release);
            try { using var won = await waiting.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { Assert.True(cancellation.IsCancellationRequested); }
            using var recovered = coordinator.Reserve(Connection("recovered")).Acquire();
        }
        using var active = coordinator.Reserve(Connection("active")).Acquire();
        var pending = coordinator.Reserve(Connection("pending")).AcquireAsync().AsTask();
        coordinator.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1, metrics.Active);
        active.Dispose();
        active.Dispose();
        Assert.Equal(0, metrics.Active);
        Assert.Equal(0, metrics.Queued);
        Assert.Equal(1, metrics.PeakActive);
        Assert.Throws<ObjectDisposedException>(() => coordinator.Reserve(Connection("closed")));
    }

    [Fact]
    public async Task RenameAndFailedClientCreation_PreserveGlobalCapacityAndSnapshot()
    {
        using var fixture = DatabaseRoleTests.Create(new(2, 3, 10) { MaxTotalConcurrent = 1 });
        using var first = fixture.Sessions.AcquireClient("primary");
        var pending = fixture.Sessions.AcquireClientAsync("analytics").AsTask();
        Assert.True(fixture.Service.RenameDatabase("analytics", "reports").Success);
        first.Dispose();
        using (var pinned = await pending.WaitAsync(TimeSpan.FromSeconds(5)))
        {
            Assert.Equal("analytics", pinned.Context.DatabaseName);
            Assert.Equal(2, pinned.Client.Ado.GetInt("select value from markers"));
        }
        var factory = fixture.Host.Services.GetRequiredService<DatabaseMcpServer.Interfaces.ISqlSugarClientFactory>();
        var coordinator = fixture.Host.Services.GetRequiredService<DatabaseExecutionCoordinator>();
        using var invalid = coordinator.Reserve(new() { Name = "invalid", DbType = "unsupported" });
        Assert.Throws<ArgumentException>(() => factory.CreateClient(invalid.Acquire()));
        using var recovered = fixture.Sessions.AcquireClient("reports");
        Assert.Equal(2, recovered.Client.Ado.GetInt("select value from markers"));
    }

    [Fact]
    public async Task RemovedAndRecreatedConnection_KeepsSnapshotAndTotalSlotUntilRelease()
    {
        using var coordinator = new DatabaseExecutionCoordinator(new(2, 4, 10) { MaxTotalConcurrent = 1 });
        var original = Connection("original");
        original.ConnectionString = "original-target";
        coordinator.UpdateConnections(new Dictionary<string, DatabaseConnection> { [original.Name] = original });
        using var snapshot = coordinator.Reserve(original);
        using var attempt = snapshot.CreateAttempt();
        using var held = attempt.Acquire();
        var identity = held.Context.ConnectionIdentity;
        coordinator.UpdateConnections(new Dictionary<string, DatabaseConnection>());
        var other = coordinator.Reserve(Connection("other")).AcquireAsync().AsTask();
        Assert.False(other.IsCompleted);

        var replacement = Connection("original");
        replacement.ConnectionString = "replacement-target";
        coordinator.UpdateConnections(new Dictionary<string, DatabaseConnection> { [replacement.Name] = replacement });
        var recreated = coordinator.Reserve(replacement).AcquireAsync().AsTask();
        Assert.False(recreated.IsCompleted);
        held.Dispose();
        using (var lease = await other.WaitAsync(TimeSpan.FromSeconds(5)))
        {
            Assert.Equal("other", lease.Context.DatabaseName);
            Assert.False(recreated.IsCompleted);
        }
        using (var lease = await recreated.WaitAsync(TimeSpan.FromSeconds(5)))
        {
            Assert.Equal(identity, lease.Context.ConnectionIdentity);
            Assert.Equal("replacement-target", lease.Context.ConnectionString);
        }
        using var retry = snapshot.CreateAttempt();
        using var pinned = await retry.AcquireAsync();
        Assert.Equal("original-target", pinned.Context.ConnectionString);
        Assert.Equal(identity, pinned.Context.ConnectionIdentity);
    }

    [Fact]
    public async Task MongoCleanup_HoldsGlobalSlotUntilNativeWorkFinishes()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fixture = MongoCollectionServiceTests.Fixture(configure: services =>
        {
            ObservedClientFactory.Register(services, db =>
            {
                if (db.CurrentConnectionConfig.DbType != SqlSugar.DbType.MongoDb) return;
                db.CurrentConnectionConfig.SqlMiddle = new SqlSugar.SqlMiddle
                {
                    IsSqlMiddle = true,
                    GetDataReaderAsync = async (_, _) =>
                    {
                        entered.TrySetResult();
                        try { await Task.Delay(Timeout.Infinite, db.Ado.CancellationToken ?? default); }
                        finally { cancelled.TrySetResult(); await cleanup.Task; }
                        throw new InvalidOperationException();
                    }
                };
            });
            services.AddSingleton(new DatabaseExecutionOptions { MaxTotalConcurrent = 1 });
        });
        using var cancellation = new CancellationTokenSource();
        var mongo = fixture.Host.Services.GetRequiredService<MongoCollectionService>()
            .ExecuteAsync(new(MongoOperation.Find, "docs"), cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var sql = fixture.Sessions.AcquireClientAsync("analytics").AsTask();
            Assert.False(sql.IsCompleted);
            cancellation.Cancel();
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(sql.IsCompleted);
            cleanup.SetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mongo);
            using var lease = await sql.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(7, lease.Client.Ado.GetInt("select 7"));
        }
        finally { cleanup.TrySetResult(); cancellation.Cancel(); }
    }

    [Theory]
    [InlineData(0, 128)]
    [InlineData(1025, 128)]
    [InlineData(32, -1)]
    [InlineData(32, 10001)]
    public void InvalidTotalLimits_FailBeforeAdmission(int active, int queued)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new DatabaseExecutionCoordinator(new() { MaxTotalConcurrent = active, MaxTotalQueued = queued }));

    [Fact]
    public async Task ConcurrentLoad_RespectsBothCapsAndDrainsMetrics()
    {
        using var metrics = new AdmissionMetrics();
        using var coordinator = new DatabaseExecutionCoordinator(new(2, 100, 10) { MaxTotalConcurrent = 3, MaxTotalQueued = 128 });
        var counts = new ConcurrentDictionary<string, int>();
        var peaks = new ConcurrentDictionary<string, int>();
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 60).Select(async index =>
        {
            var name = "db" + index % 4;
            using var lease = await coordinator.Reserve(Connection(name)).AcquireAsync();
            var current = counts.AddOrUpdate(name, 1, (_, value) => value + 1);
            peaks.AddOrUpdate(name, current, (_, value) => Math.Max(value, current));
            await hold.Task;
            await Task.Yield();
            counts.AddOrUpdate(name, 0, (_, value) => value - 1);
        }).ToArray();
        Assert.Equal(3, metrics.Active);
        Assert.Equal(57, metrics.Queued);
        hold.SetResult();
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(3, metrics.PeakActive);
        Assert.All(peaks.Values, value => Assert.InRange(value, 1, 2));
        Assert.All(counts.Values, value => Assert.Equal(0, value));
        Assert.Equal(0, metrics.Active);
        Assert.Equal(0, metrics.Queued);
    }

    private static DatabaseConnection Connection(string name) => new() { Name = name, DbType = "Sqlite", ConnectionString = "Data Source=:memory:" };

    private sealed class AdmissionMetrics : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly object _gate = new();
        public long Active { get; private set; }
        public long Queued { get; private set; }
        public long PeakActive { get; private set; }
        public Action? OnQueued { get; set; }
        public AdmissionMetrics()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            { if (instrument.Meter.Name == DatabaseTelemetry.MeterName) listener.EnableMeasurementEvents(instrument); };
            _listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
            {
                lock (_gate)
                {
                    if (instrument.Name == "dbmcp.active") { Active += value; PeakActive = Math.Max(PeakActive, Active); }
                    if (instrument.Name == "dbmcp.queued") Queued += value;
                }
                if (instrument.Name == "dbmcp.queued" && value > 0) OnQueued?.Invoke();
            });
            _listener.Start();
        }
        public void Dispose() => _listener.Dispose();
    }
}
