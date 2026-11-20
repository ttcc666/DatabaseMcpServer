using System.Data.Common;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using DatabaseMcpServer.Strategies.DBSetting;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;

namespace DatabaseMcpServer.Tests;

public class ClientLeaseTests
{
    [Fact]
    public async Task RetiredLease_SurvivesReloadAndDisposesOnReturnAcrossThreads()
    {
        using var factory = CreateFactory();
        var lease = factory.AcquireClient(Connection());
        var connection = (DbConnection)lease.Client.Ado.Connection;
        var disposed = 0;
        connection.Disposed += (_, _) => Interlocked.Increment(ref disposed);
        var changed = Connection("Data Source=:memory:;Cache=Shared");
        await Task.Run(() => factory.UpdateConnections(new Dictionary<string, DatabaseConnection> { [changed.Name] = changed }));
        Assert.Equal(0, disposed);
        Assert.Equal(7, await Task.Run(() => lease.Client.Ado.GetInt("select 7")));
        using var next = factory.AcquireClient(changed);
        Assert.NotSame(lease.Client, next.Client);
        await Task.Run(lease.Dispose);
        lease.Dispose();
        Assert.Equal(1, disposed);
    }

    [Fact]
    public void RepeatedReloads_ReleaseEveryRetiredClient()
    {
        using var factory = CreateFactory();
        var disposed = 0;
        for (var i = 0; i < 100; i++)
        {
            using (var lease = factory.AcquireClient(Connection()))
            {
                ((DbConnection)lease.Client.Ado.Connection).Disposed += (_, _) => disposed++;
                Assert.Equal(i, lease.Client.Ado.GetInt($"select {i}"));
            }
            factory.UpdateConnections(new Dictionary<string, DatabaseConnection>());
            Assert.Equal(i + 1, disposed);
        }
    }

    [Fact]
    public void Shutdown_DefersActiveLeaseAndRejectsNewRentals()
    {
        var factory = CreateFactory();
        var lease = factory.AcquireClient(Connection());
        var disposed = 0;
        ((DbConnection)lease.Client.Ado.Connection).Disposed += (_, _) => disposed++;
        factory.Dispose();
        Assert.Equal(0, disposed);
        Assert.Equal(1, lease.Client.Ado.GetInt("select 1"));
        Assert.Throws<ObjectDisposedException>(() => factory.AcquireClient(Connection()));
        Assert.Throws<ObjectDisposedException>(() => factory.ReserveClient(Connection()));
        lease.Dispose();
        factory.Dispose();
        Assert.Equal(1, disposed);
    }

    [Fact]
    public void MetadataReload_CreatesFreshContextWithNewPermissions()
    {
        using var factory = CreateFactory();
        var original = Connection();
        using var first = factory.AcquireClient(original);
        first.Dispose();
        var changed = Connection();
        changed.EnableDangerousOperations = true;
        changed.Description = "updated";
        factory.UpdateConnections(new Dictionary<string, DatabaseConnection> { [changed.Name] = changed });
        using var next = factory.AcquireClient(changed);
        Assert.NotSame(first.Client, next.Client);
        Assert.True(next.Context.EnableDangerousOperations);
        Assert.False(first.Context.EnableDangerousOperations);
    }

    [Fact]
    public void ConcurrentRentals_AreExclusiveAndSurviveRemoval()
    {
        using var factory = CreateFactory();
        using var first = factory.AcquireClient(Connection());
        using var second = factory.AcquireClient(Connection());
        Assert.NotSame(first.Client, second.Client);
        factory.UpdateConnections(new Dictionary<string, DatabaseConnection>());
        Assert.Equal(4, first.Client.Ado.GetInt("select 4"));
        Assert.Equal(5, second.Client.Ado.GetInt("select 5"));
    }

    [Fact]
    public async Task FailedTool_DisposesContextAndReleasesCapacity()
    {
        var disposed = 0;
        using var fixture = new RuntimeConsistencyTests.Fixture(client =>
            ((DbConnection)client.Ado.Connection).Disposed += (_, _) => disposed++);
        var tools = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<DatabaseMcpServer.Tools.Query.DatabaseQueryTools>(fixture.Host.Services);
        using var failed = System.Text.Json.JsonDocument.Parse(await tools.QueryDatabase("select * from missing_table").Json());
        Assert.False(failed.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(1, disposed);
        using (var next = fixture.Service.AcquireClient()) Assert.Equal(9, next.Client.Ado.GetInt("select 9"));
        Assert.Equal(2, disposed);
    }

    [Fact]
    public void ReturnedContext_DoesNotLeakAdoOrAopState()
    {
        using var factory = CreateFactory();
        using var cancellation = new CancellationTokenSource();
        using var first = factory.AcquireClient(Connection());
        var defaultTimeout = first.Client.Ado.CommandTimeOut;
        var oldCalls = 0;
        first.Client.Aop.OnLogExecuting = (_, _) => oldCalls++;
        first.Client.Ado.CommandTimeOut = 1;
        first.Client.Ado.CancellationToken = cancellation.Token;
        first.Client.Ado.UseStoredProcedure();
        first.Dispose();
        cancellation.Cancel();

        using var next = factory.AcquireClient(Connection());
        Assert.NotSame(first.Client, next.Client);
        Assert.Equal(defaultTimeout, next.Client.Ado.CommandTimeOut);
        Assert.Equal(System.Data.CommandType.Text, next.Client.Ado.CommandType);
        Assert.NotEqual(cancellation.Token, next.Client.Ado.CancellationToken);
        Assert.Equal(23, next.Client.Ado.GetInt("select 23"));
        Assert.Equal(0, oldCalls);
    }

    private static SqlSugarClientFactory CreateFactory() => new(NullLogger<SqlSugarClientFactory>.Instance,
        new DatabaseHelper(NullLogger<DatabaseHelper>.Instance), new DatabaseOptimizationStrategyFactory(), new DatabaseExecutionOptions());
    private static DatabaseConnection Connection(string connectionString = "Data Source=:memory:") => new()
    { Name = "primary", DbType = "Sqlite", ConnectionString = connectionString };
}
