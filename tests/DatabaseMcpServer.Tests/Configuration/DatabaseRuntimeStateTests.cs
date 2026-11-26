using System.Text.Json;
using DatabaseMcpServer.Configuration;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DatabaseMcpServer.Tests;

public class DatabaseRuntimeStateTests
{
    [Fact]
    public void CursorBinding_UsesAppliedSnapshotAndSurvivesRestart()
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        var query = new { tableName = "items", pageSize = 1 };
        using var reservation = fixture.Service.ReserveConnection();
        using var original = fixture.Service.ReserveConnection().Acquire();
        var binding = KeysetCursor.Bind(original.Context, query);
        using (var restarted = fixture.BuildHost())
        using (var fresh = restarted.Services.GetRequiredService<IDatabaseConfigService>().ReserveConnection().Acquire())
        {
            Assert.NotEqual(original.Context.ConnectionIdentity, fresh.Context.ConnectionIdentity);
            Assert.Equal(binding, KeysetCursor.Bind(fresh.Context, query));
        }
        File.AppendAllText(fixture.Path, "\n ");
        Assert.True(fixture.Service.GetRuntimeStatus().HasPendingChanges);
        using (var unchanged = fixture.Service.ReserveConnection().Acquire())
            Assert.Equal(binding, KeysetCursor.Bind(unchanged.Context, query));
        Assert.True(fixture.Service.ReloadConfiguration().Success);
        using var pinned = reservation.Acquire();
        using var current = fixture.Service.ReserveConnection().Acquire();
        Assert.Equal(binding, KeysetCursor.Bind(pinned.Context, query));
        Assert.NotEqual(binding, KeysetCursor.Bind(current.Context, query));
        Assert.DoesNotContain("Fingerprint", JsonSerializer.Serialize(current.Context));
        Assert.DoesNotContain("ConnectionString", JsonSerializer.Serialize(current.Context));
    }

    [Fact]
    public void RuntimeStatus_TracksObservedAndAppliedContentExactly()
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        var service = fixture.Service;
        var original = File.ReadAllText(fixture.Path);
        var notifications = 0;
        fixture.Host.Services.GetRequiredService<ConfigurationChangeSignal>().Changed += () => notifications++;
        AssertStatus(service, 1, 1, false);
        AssertStatus(service, 1, 1, false);

        // A whitespace-only edit changes the saved content hash, but not the live snapshot.
        File.WriteAllText(fixture.Path, original + "\n ");
        AssertStatus(service, 1, 2, true);
        AssertStatus(service, 1, 2, true);
        using var pinned = service.ReserveConnection().Acquire();
        Assert.Equal(1, pinned.Context.ConfigurationVersion);
        Assert.True(service.ReloadConfiguration().Success);
        AssertStatus(service, 2, 2, false);
        Assert.Equal(1, pinned.Context.ConfigurationVersion);
        using (var current = service.ReserveConnection().Acquire())
            Assert.Equal(2, current.Context.ConfigurationVersion);
        Assert.Equal(1, notifications);

        File.WriteAllText(fixture.Path, "{");
        AssertStatus(service, 2, 3, true);
        Assert.False(service.ReloadConfiguration().RuntimeApplied);
        AssertStatus(service, 2, 3, true);
        Assert.Equal("primary", service.GetCurrentDatabaseName());
        Assert.Equal(1, notifications);

        File.Delete(fixture.Path);
        AssertStatus(service, 2, 4, true);
        AssertStatus(service, 2, 4, true);
        File.WriteAllText(fixture.Path, original);
        AssertStatus(service, 2, 5, true);
        Assert.True(service.ReloadConfiguration().Success);
        AssertStatus(service, 5, 5, false);
        Assert.True(service.SwitchDatabase("analytics"));
        AssertStatus(service, 5, 5, false);
        Assert.Equal("analytics", service.GetCurrentDatabaseName());
        Assert.Equal(2, notifications);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RuntimeStatus_ReadFailureKeepsVersionsAndCurrentDatabase(bool unauthorized)
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        var failRead = false;
        var service = new DatabaseConfigService(NullLogger<DatabaseConfigService>.Instance,
            fixture.Host.Services.GetRequiredService<IDatabaseHelperService>(),
            fixture.Host.Services.GetRequiredService<DatabaseExecutionCoordinator>(),
            fixture.Host.Services.GetRequiredService<DatabaseConfigurationApplicationService>(),
            fixture.Host.Services.GetRequiredService<ICurrentDatabaseStateStore>(),
            new DatabaseRuntimeOptions(false, fixture.Path),
            readConfig: path =>
            {
                if (!failRead) return File.ReadAllText(path);
                if (unauthorized) throw new UnauthorizedAccessException("read denied");
                throw new IOException("read failed");
            });
        failRead = true;
        var failed = service.GetRuntimeStatus();
        Assert.Equal("primary", failed.CurrentDatabase);
        Assert.Equal((1L, 1L), (failed.RuntimeVersion, failed.SavedVersion));
        Assert.Null(failed.HasPendingChanges);
        Assert.False(service.ReloadConfiguration().RuntimeApplied);
        Assert.Equal(2, service.GetAllConnections().Count);
        failRead = false;
        AssertStatus(service, 1, 1, false);
    }

    [Fact]
    public void Rename_AdmissionConflictDoesNotPublishStateOrNotification()
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        var service = fixture.Service;
        using var oldTarget = service.ReserveConnection("analytics").Acquire();
        using var primary = service.ReserveConnection("primary").Acquire();
        var saved = new ConfigurationFileStore().Load(fixture.Path);
        saved.Databases.RemoveAll(connection => connection.Name == "analytics");
        File.WriteAllText(fixture.Path, JsonSerializer.Serialize(saved));
        Assert.True(service.ReloadConfiguration().Success);
        AssertStatus(service, 2, 2, false);
        var notifications = 0;
        fixture.Host.Services.GetRequiredService<ConfigurationChangeSignal>().Changed += () => notifications++;

        var result = service.RenameDatabase("primary", "analytics");
        Assert.False(result.Success);
        Assert.True(result.Saved);
        Assert.False(result.RuntimeApplied);
        Assert.Null(result.SelectionPersisted);
        Assert.Equal("analytics", result.DatabaseName);
        Assert.Equal("analytics", Assert.Single(new ConfigurationFileStore().Load(fixture.Path).Databases).Name);
        Assert.Equal("primary", Assert.Single(service.GetAllConnections()).Name);
        Assert.Equal("primary", service.GetCurrentDatabaseName());
        AssertStatus(service, 2, 3, true);
        Assert.Equal(0, notifications);
        Assert.NotEqual(primary.Context.ConnectionIdentity, oldTarget.Context.ConnectionIdentity);

        oldTarget.Dispose();
        Assert.True(service.ReloadConfiguration().Success);
        AssertStatus(service, 3, 3, false);
        Assert.Equal(1, notifications);
        using var renamed = service.ReserveConnection("analytics").Acquire();
        Assert.Equal(primary.Context.ConnectionIdentity, renamed.Context.ConnectionIdentity);
        Assert.Equal(3, renamed.Context.ConfigurationVersion);
        Assert.Equal("primary", primary.Context.DatabaseName);
        Assert.Equal(1, primary.Context.ConfigurationVersion);
    }

    private static void AssertStatus(IDatabaseConfigService service, long runtime, long saved, bool pending)
    {
        var status = service.GetRuntimeStatus();
        Assert.Equal((runtime, saved, (bool?)pending), (status.RuntimeVersion, status.SavedVersion, status.HasPendingChanges));
    }
}
