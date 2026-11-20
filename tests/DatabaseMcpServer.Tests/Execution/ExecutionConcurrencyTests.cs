using DatabaseMcpServer.Configuration;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using DatabaseMcpServer.Strategies.DBSetting;
using Microsoft.Extensions.Logging.Abstractions;

namespace DatabaseMcpServer.Tests;

public class ExecutionConcurrencyTests
{
    [Fact]
    public async Task Queue_CancelsRejectsOverflowAndReturnsCapacity()
    {
        using var factory = Factory();
        using var active = factory.AcquireClient(Connection());
        using var cancel = new CancellationTokenSource();
        using var reservation = factory.ReserveClient(Connection());
        var waiting = reservation.AcquireAsync(cancel.Token).AsTask();
        Assert.False(waiting.IsCompleted);
        using var overflow = factory.ReserveClient(Connection());
        var error = await Assert.ThrowsAsync<DatabaseMcpException>(() => overflow.AcquireAsync().AsTask());
        Assert.Equal(DatabaseErrorCode.DatabaseBusy, error.ErrorCode);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        active.Dispose();
        using var next = await factory.ReserveClient(Connection()).AcquireAsync();
        Assert.Equal(7, next.Client.Ado.GetInt("select 7"));
    }

    [Fact]
    public async Task ReloadAndRename_KeepOldTargetAndShareAdmission()
    {
        using var factory = Factory();
        var original = Connection();
        using var first = factory.AcquireClient(original);
        using var reserved = factory.ReserveClient(original);
        var waiting = reserved.AcquireAsync().AsTask();
        var changed = Connection("renamed");
        changed.ConnectionString = "Data Source=:memory:;Cache=Shared";
        changed.EnableDangerousOperations = true;
        factory.UpdateConnections(new Dictionary<string, DatabaseConnection> { [changed.Name] = changed }, original.Name, changed.Name);
        using var renamed = factory.ReserveClient(changed);
        Assert.Equal(DatabaseErrorCode.DatabaseBusy,
            (await Assert.ThrowsAsync<DatabaseMcpException>(() => renamed.AcquireAsync().AsTask())).ErrorCode);
        first.Dispose();
        using var second = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("primary", second.Context.DatabaseName);
        Assert.Equal(original.ConnectionString, second.Client.CurrentConnectionConfig.ConnectionString);
        Assert.False(second.Context.EnableDangerousOperations);
        second.Dispose();
        using var next = await factory.ReserveClient(changed).AcquireAsync();
        Assert.Equal("renamed", next.Context.DatabaseName);
        Assert.True(next.Context.EnableDangerousOperations);
        Assert.Equal(changed.ConnectionString, next.Client.CurrentConnectionConfig.ConnectionString);
    }

    [Fact]
    public async Task Shutdown_CancelsWaitingButKeepsActiveLeaseUntilReturn()
    {
        using var factory = Factory();
        using var active = factory.AcquireClient(Connection());
        var waiting = factory.ReserveClient(Connection()).AcquireAsync().AsTask();
        factory.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.Equal(5, active.Client.Ado.GetInt("select 5"));
        Assert.Throws<ObjectDisposedException>(() => factory.ReserveClient(Connection()));
    }

    [Fact]
    public async Task QueueTimeout_DoesNotConsumeActiveCapacity()
    {
        using var factory = Factory(new(1, 1, 1));
        using var active = factory.AcquireClient(Connection());
        var error = await Assert.ThrowsAsync<DatabaseMcpException>(() => factory.ReserveClient(Connection()).AcquireAsync().AsTask());
        Assert.Equal(DatabaseErrorCode.DatabaseBusy, error.ErrorCode);
        active.Dispose();
        using var next = factory.AcquireClient(Connection());
        Assert.Equal(1, next.Client.Ado.GetInt("select 1"));
    }

    [Fact]
    public async Task DifferentDatabases_DoNotShareLimitsAndSnapshotsAreDefensive()
    {
        using var factory = Factory();
        using var first = factory.AcquireClient(Connection());
        using var other = await factory.ReserveClient(Connection("other")).AcquireAsync();
        Assert.Equal("other", other.Context.DatabaseName);
        using var fixture = new RuntimeConsistencyTests.Fixture();
        var config = new DatabaseMcpServer.Configuration.ConfigurationFileStore().Load(fixture.Path);
        config.Databases[0].OptimizationSettings = new() { ["SqliteEnableDefaultValue"] = "true" };
        File.WriteAllText(fixture.Path, System.Text.Json.JsonSerializer.Serialize(config));
        Assert.True(fixture.Service.ReloadConfiguration().Success);
        var connections = fixture.Service.GetAllConnections();
        connections[0].Name = "corrupted";
        connections[0].EnableDangerousOperations = true;
        connections[0].OptimizationSettings!["SqliteEnableDefaultValue"] = "false";
        Assert.Equal("primary", fixture.Service.GetCurrentDatabaseName());
        Assert.False(fixture.Service.GetAllConnections()[0].EnableDangerousOperations);
        Assert.Equal("true", fixture.Service.GetAllConnections()[0].OptimizationSettings!["SqliteEnableDefaultValue"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RenameIntoRetiredName_RetryPreservesSourceAdmission(bool renameAgain)
    {
        using var factory = Factory();
        var source = Connection();
        var target = Connection("retired");
        using var sourceLease = factory.AcquireClient(source);
        using var targetLease = factory.AcquireClient(target);
        factory.UpdateConnections(new Dictionary<string, DatabaseConnection> { [source.Name] = source });
        var replacement = new Dictionary<string, DatabaseConnection> { [target.Name] = target };
        Assert.Equal(DatabaseErrorCode.DatabaseBusy,
            Assert.Throws<DatabaseMcpException>(() => factory.UpdateConnections(replacement, source.Name, target.Name)).ErrorCode);
        if (renameAgain)
        {
            var previousName = target.Name;
            target = Connection("final");
            factory.UpdateConnections(new Dictionary<string, DatabaseConnection> { [target.Name] = target }, previousName, target.Name);
        }
        else
        {
            targetLease.Dispose();
            // A later ordinary reload must remember the interrupted rename's admission identity.
            factory.UpdateConnections(replacement);
        }
        var waiting = factory.ReserveClient(target).AcquireAsync().AsTask();
        Assert.False(waiting.IsCompleted);
        sourceLease.Dispose();
        using var next = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(renameAgain ? "final" : "retired", next.Context.DatabaseName);
    }

    [Fact]
    public async Task WaitingRequest_KeepsTargetAndPermissionsAcrossSwitchAndReload()
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        var leases = Enumerable.Range(0, 8).Select(_ => fixture.Sessions.AcquireClient()).ToArray();
        try
        {
            var waiting = fixture.Sessions.AcquireClientAsync().AsTask();
            Assert.False(waiting.IsCompleted);
            Assert.True(fixture.Service.SwitchDatabase("analytics"));
            Assert.True(fixture.Service.UpdateDatabase("primary", new()
                { ApplyEnableDangerousOperations = true, EnableDangerousOperations = true }).Success);
            leases[0].Dispose();
            using var original = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("primary", original.Context.DatabaseName);
            Assert.False(original.Context.EnableDangerousOperations);
            using var current = await fixture.Sessions.AcquireClientAsync();
            Assert.Equal("analytics", current.Context.DatabaseName);
            Assert.False(current.Context.EnableDangerousOperations);
        }
        finally { foreach (var lease in leases) lease.Dispose(); }
    }

    [Fact]
    public async Task FailedCreationAndAbandonedReservation_DoNotLeakCapacity()
    {
        using var factory = Factory(new(1, 0, 10));
        using (var abandoned = factory.ReserveClient(Connection())) { }
        var invalid = Connection();
        invalid.DbType = "unsupported";
        await Assert.ThrowsAsync<ArgumentException>(() => factory.ReserveClient(invalid).AcquireAsync().AsTask());
        using var next = await factory.ReserveClient(Connection()).AcquireAsync();
        Assert.Equal(3, next.Client.Ado.GetInt("select 3"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(32)]
    public async Task ConfiguredConcurrency_AdmitsExactlyLimitAndDrainsQueue(int limit)
    {
        using var factory = Factory(new(limit, 32, 10));
        var leases = Enumerable.Range(0, limit).Select(_ => factory.AcquireClient(Connection())).ToArray();
        try
        {
            var queued = Enumerable.Range(0, 32).Select(_ => factory.ReserveClient(Connection()).AcquireAsync().AsTask()).ToArray();
            Assert.All(queued, request => Assert.False(request.IsCompleted));
            Assert.Equal(DatabaseErrorCode.DatabaseBusy, (await Assert.ThrowsAsync<DatabaseMcpException>(
                () => factory.ReserveClient(Connection()).AcquireAsync().AsTask())).ErrorCode);
            foreach (var lease in leases) lease.Dispose();
            await Task.WhenAll(queued.Select(async request =>
            {
                using var lease = await request.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(19, lease.Client.Ado.GetInt("select 19"));
            }));
        }
        finally { foreach (var lease in leases) lease.Dispose(); }
    }

    private static SqlSugarClientFactory Factory(DatabaseExecutionOptions? options = null) => new(
        NullLogger<SqlSugarClientFactory>.Instance,
        new DatabaseOptimizationStrategyFactory(), options ?? new(1, 1, 10));
    private static DatabaseConnection Connection(string name = "primary") => new()
        { Name = name, DbType = "Sqlite", ConnectionString = "Data Source=:memory:" };
}
