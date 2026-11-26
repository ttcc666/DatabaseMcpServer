using System.Text.Json;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using DatabaseMcpServer.Tools.Management;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseMcpServer.Tests;

public class ConnectionRetryTests
{
    [Fact]
    public async Task RetryBackoff_AllowsAnotherRequestAndKeepsTargetAcrossReloadAndRename()
    {
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attemptCount = 0;
        var targets = new List<string>();
        using var fixture = new RuntimeConsistencyTests.Fixture(client =>
        {
            if (client.CurrentConnectionConfig.ConnectionString.Contains("original", StringComparison.Ordinal))
            {
                targets.Add(client.CurrentConnectionConfig.ConnectionString);
                if (Interlocked.Increment(ref attemptCount) == 1)
                {
                    failed.TrySetResult();
                    throw new TimeoutException("Transient first connection failure");
                }
            }
        }, services => services.AddSingleton(new DatabaseExecutionOptions { MaxConcurrent = 1, MaxQueued = 0 }));
        var original = $"Data Source={Path.Combine(fixture.Directory.FullName, "original.db")};Pooling=False";
        Assert.True(fixture.Service.UpdateDatabase("primary", new() { ApplyConnectionString = true, ConnectionString = original }).Success);
        var tool = fixture.Host.Services.GetRequiredService<ConnectionTools>();
        var pending = tool.TestConnection(databaseName: "primary", maxRetries: 1, initialDelayMs: 1000);
        await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(fixture.Service.RenameDatabase("primary", "renamed").Success);
        Assert.True(fixture.Service.UpdateDatabase("renamed", new() { ApplyConnectionString = true, ConnectionString = "Data Source=:memory:" }).Success);
        using (var other = fixture.Sessions.AcquireClient("renamed")) Assert.NotEqual(original, other.Client.CurrentConnectionConfig.ConnectionString);
        var response = DatabaseRoleTests.Success(await pending);
        Assert.Equal(2, response.GetProperty("attempts").GetInt32());
        Assert.Equal("primary", response.GetProperty("currentDatabase").GetString());
        Assert.Equal(new[] { original, original }, targets);
    }

    [Fact]
    public async Task Snapshot_ReleasesSlotsBetweenAttemptsAndSurvivesRename()
    {
        using var coordinator = new DatabaseExecutionCoordinator(new() { MaxConcurrent = 1, MaxQueued = 0 });
        var connection = new DatabaseConnection { Name = "original", DbType = "Sqlite", ConnectionString = "original-target" };
        coordinator.UpdateConnections(new Dictionary<string, DatabaseConnection> { [connection.Name] = connection });
        using var snapshot = coordinator.Reserve(connection);
        string identity;
        using (var first = snapshot.CreateAttempt())
        using (var lease = await first.AcquireAsync()) identity = lease.Context.ConnectionIdentity;
        var renamed = new DatabaseConnection { Name = "renamed", DbType = "Sqlite", ConnectionString = "new-target" };
        coordinator.UpdateConnections(new Dictionary<string, DatabaseConnection> { [renamed.Name] = renamed }, "original", "renamed");
        using (var other = coordinator.Reserve(renamed))
        using (var lease = await other.AcquireAsync()) Assert.Equal(identity, lease.Context.ConnectionIdentity);
        using var next = snapshot.CreateAttempt();
        using var retry = await next.AcquireAsync();
        Assert.Equal("original-target", retry.Context.ConnectionString);
        Assert.Equal("original", retry.Context.DatabaseName);
        Assert.Equal(identity, retry.Context.ConnectionIdentity);
    }

    [Fact]
    public async Task CanonicalTestConnection_HonorsNamedTargetAndRejectsInvalidDeadline()
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        var tool = fixture.Host.Services.GetRequiredService<ConnectionTools>();
        using var response = TestJson.Parse(await tool.TestConnection(databaseName: "analytics"));
        Assert.True(response.RootElement.GetProperty("connected").GetBoolean());
        Assert.Equal("analytics", response.RootElement.GetProperty("currentDatabase").GetString());
        Assert.Equal("primary", fixture.Service.GetCurrentDatabaseName());
        using var invalid = TestJson.Parse(await tool.TestConnection(totalTimeoutSeconds: 0));
        Assert.Equal(1004, invalid.RootElement.GetProperty("errorCode").GetInt32());
        using var missing = TestJson.Parse(await tool.TestConnection(databaseName: "missing"));
        Assert.False(missing.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public void RetryClassification_DoesNotRetryAuthenticationOrAdmissionErrors()
    {
        Assert.True(DatabaseConnectionProbe.IsTransient(new TimeoutException()));
        Assert.False(DatabaseConnectionProbe.IsTransient(new UnauthorizedAccessException()));
        Assert.False(DatabaseConnectionProbe.IsTransient(new DatabaseMcpException(DatabaseErrorCode.DatabaseBusy, "busy")));
        Assert.False(DatabaseConnectionProbe.IsTransient(new ArgumentException()));
    }

    [Fact]
    public async Task RetryExhaustion_UsesStructuredConnectionError()
    {
        using var fixture = new RuntimeConsistencyTests.Fixture(_ => throw new TimeoutException("test failure"));
        using var result = TestJson.Parse(await fixture.Host.Services.GetRequiredService<ConnectionTools>().TestConnection(maxRetries: 1, initialDelayMs: 1));
        Assert.False(result.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(2, result.RootElement.GetProperty("attempts").GetInt32());
        Assert.Equal(1001, result.RootElement.GetProperty("errorCode").GetInt32());
        Assert.False(result.RootElement.GetProperty("retryable").GetBoolean());
        Assert.Equal("connect", result.RootElement.GetProperty("phase").GetString());
    }
}
