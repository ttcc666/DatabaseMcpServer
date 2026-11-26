using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DatabaseMcpServer.Tests;

public partial class DatabaseConfigFileMonitorServiceTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void FileChange_AppliedContentIsDeduplicated(bool success, bool runtimeApplied)
    {
        using var fixture = new MonitorFixture();
        fixture.Config.Reload = () => new() { Success = success, RuntimeApplied = runtimeApplied };

        fixture.Monitor.HandleMonitoredFileChangedForTests();
        fixture.Monitor.HandleMonitoredFileChangedForTests();

        Assert.Equal(1, fixture.Config.ReloadCount);
        Assert.True(fixture.Config.FollowFileDefault);
        // The duplicate returns before the post-reload settings check.
        Assert.Equal(3, fixture.Config.SettingsChecks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FileChange_FailedReloadRetriesSameContent(bool throws)
    {
        using var fixture = new MonitorFixture();
        fixture.Config.Reload = () => throws ? throw new IOException("reload failed") : new();

        fixture.Monitor.HandleMonitoredFileChangedForTests();
        fixture.Monitor.HandleMonitoredFileChangedForTests();
        Assert.Equal(2, fixture.Config.ReloadCount);

        fixture.Config.Reload = () => new() { Success = true };
        fixture.Monitor.HandleMonitoredFileChangedForTests();
        fixture.Monitor.HandleMonitoredFileChangedForTests();
        Assert.Equal(3, fixture.Config.ReloadCount);
    }

    [Fact]
    public void FileChange_UnreadableContentDoesNotDeduplicateNullHash()
    {
        using var fixture = new MonitorFixture();
        File.Delete(fixture.Path);

        fixture.Monitor.HandleMonitoredFileChangedForTests();
        fixture.Monitor.HandleMonitoredFileChangedForTests();

        Assert.Equal(2, fixture.Config.ReloadCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FileChange_DisabledMonitoringStopsResources(bool disabledDuringReload)
    {
        using var fixture = new MonitorFixture();
        await fixture.Monitor.StartAsync(CancellationToken.None);
        fixture.Config.Path = System.IO.Path.Combine(fixture.Directory.FullName, "next.json");
        File.WriteAllText(fixture.Config.Path, "next");
        fixture.Config.Enabled = disabledDuringReload;
        fixture.Config.Reload = () =>
        {
            fixture.Config.Enabled = false;
            return new() { Success = true };
        };

        fixture.Monitor.HandleMonitoredFileChangedForTests();

        Assert.Null(fixture.Monitor.MonitoredPathForTests);
        Assert.Equal(disabledDuringReload ? 1 : 0, fixture.Config.ReloadCount);
    }

    [Fact]
    public async Task Start_InitialContentIsDeduplicated_AndSamePathDoesNotResetHash()
    {
        using var fixture = new MonitorFixture();
        await fixture.Monitor.StartAsync(CancellationToken.None);
        fixture.Monitor.HandleMonitoredFileChangedForTests();
        Assert.Equal(0, fixture.Config.ReloadCount);

        // Change the published hash without generating a filesystem event at the watched path.
        fixture.Config.Path = System.IO.Path.Combine(fixture.Directory.FullName, "next.json");
        File.WriteAllText(fixture.Config.Path, "next");
        fixture.Monitor.HandleMonitoredFileChangedForTests();
        Assert.Equal(1, fixture.Config.ReloadCount);
        fixture.Config.Path = fixture.Path;
        fixture.Monitor.EnsureMonitorStarted();
        fixture.Monitor.HandleMonitoredFileChangedForTests();

        Assert.Equal(2, fixture.Config.ReloadCount);
        Assert.Equal(fixture.Path, fixture.Monitor.MonitoredPathForTests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lifecycle_OnlyStopAllowsRestart(bool dispose)
    {
        using var fixture = new MonitorFixture();
        await fixture.Monitor.StartAsync(CancellationToken.None);
        Assert.Equal(fixture.Path, fixture.Monitor.MonitoredPathForTests);

        if (dispose) fixture.Monitor.Dispose();
        else await fixture.Monitor.StopAsync(CancellationToken.None);
        Assert.Null(fixture.Monitor.MonitoredPathForTests);

        fixture.Monitor.EnsureMonitorStarted();
        Assert.Equal(dispose ? null : fixture.Path, fixture.Monitor.MonitoredPathForTests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lifecycle_DoesNotWaitForInFlightReload(bool dispose)
    {
        using var fixture = new MonitorFixture();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await fixture.Monitor.StartAsync(CancellationToken.None);
        fixture.Config.Path = System.IO.Path.Combine(fixture.Directory.FullName, "next.json");
        File.WriteAllText(fixture.Config.Path, "next");
        fixture.Config.Reload = () =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Reload was not released.");
            return new() { Success = true };
        };

        var reload = Task.Run(fixture.Monitor.HandleMonitoredFileChangedForTests);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Run(async () =>
            {
                if (dispose) fixture.Monitor.Dispose();
                else await fixture.Monitor.StopAsync(CancellationToken.None);
            }).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(fixture.Monitor.MonitoredPathForTests);
            Assert.False(reload.IsCompleted);
        }
        finally
        {
            release.Set();
            await reload.WaitAsync(TimeSpan.FromSeconds(5));
        }

        fixture.Monitor.HandleMonitoredFileChangedForTests();
        Assert.Equal(1, fixture.Config.ReloadCount);
        File.WriteAllText(fixture.Config.Path, "after stop");
        fixture.Monitor.HandleMonitoredFileChangedForTests();
        Assert.Equal(dispose ? 1 : 2, fixture.Config.ReloadCount);
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("created")]
    [InlineData("renamed")]
    public async Task FileSystemEvents_ApplyNewContent(string kind)
    {
        using var fixture = new MonitorFixture();
        var applied = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (kind != "changed") File.Delete(fixture.Path);
        fixture.Config.Reload = () =>
        {
            applied.TrySetResult(File.ReadAllText(fixture.Path));
            return new() { Success = true };
        };
        await fixture.Monitor.StartAsync(CancellationToken.None);

        if (kind == "renamed")
        {
            var staging = System.IO.Path.Combine(fixture.Directory.FullName, "staging.json");
            File.WriteAllText(staging, "new content");
            File.Move(staging, fixture.Path);
        }
        else
        {
            File.WriteAllText(fixture.Path, "new content");
        }

        Assert.Equal("new content", await applied.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(fixture.Config.FollowFileDefault);
    }

    [Fact]
    public async Task ScheduleApply_FiltersNamesAndAppliesRepeatedMatchingEvents()
    {
        using var fixture = new MonitorFixture();
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.False(fixture.Monitor.ScheduleApplyForTests(fixture.Path));
        await fixture.Monitor.StartAsync(CancellationToken.None);
        fixture.Config.Path = System.IO.Path.Combine(fixture.Directory.FullName, "next.json");
        File.WriteAllText(fixture.Config.Path, "next");
        fixture.Config.Reload = () =>
        {
            applied.TrySetResult();
            return new() { Success = true };
        };

        Assert.False(fixture.Monitor.ScheduleApplyForTests(fixture.Config.Path));
        Assert.Equal(0, fixture.Config.ReloadCount);
        Assert.True(fixture.Monitor.ScheduleApplyForTests(fixture.Path));
        Assert.True(fixture.Monitor.ScheduleApplyForTests(fixture.Path.ToUpperInvariant()));
        Assert.True(fixture.Monitor.ScheduleApplyForTests(fixture.Path));
        await applied.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(fixture.Config.FollowFileDefault);

        await fixture.Monitor.StopAsync(CancellationToken.None);
        Assert.False(fixture.Monitor.ScheduleApplyForTests(fixture.Path));
        fixture.Monitor.Dispose();
        Assert.False(fixture.Monitor.ScheduleApplyForTests(fixture.Path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FileChange_InvalidOrMissingFilePreservesRuntimeUntilRepair(bool missing)
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        var config = new DatabaseConfigService(NullLogger<DatabaseConfigService>.Instance,
            fixture.Host.Services.GetRequiredService<IDatabaseHelperService>(),
            fixture.Host.Services.GetRequiredService<DatabaseExecutionCoordinator>(),
            fixture.Host.Services.GetRequiredService<DatabaseConfigurationApplicationService>(),
            fixture.Host.Services.GetRequiredService<ICurrentDatabaseStateStore>(),
            new DatabaseRuntimeOptions(true, fixture.Path));
        using var monitor = new DatabaseConfigFileMonitorService(config, NullLogger<DatabaseConfigFileMonitorService>.Instance);
        Assert.Equal("primary", config.GetCurrentDatabaseName());
        if (missing) File.Delete(fixture.Path);
        else File.WriteAllText(fixture.Path, "{");

        monitor.HandleMonitoredFileChangedForTests();
        Assert.Equal("primary", config.GetCurrentDatabaseName());
        Assert.Equal(2, config.GetAllConnections().Count);

        File.WriteAllText(fixture.Path, """
            {"databases":[{"name":"repaired","dbType":"Sqlite","connectionString":"Data Source=:memory:","isDefault":true}]}
            """);
        monitor.HandleMonitoredFileChangedForTests();
        Assert.Equal("repaired", config.GetCurrentDatabaseName());
        Assert.Single(config.GetAllConnections());
    }

    private sealed class MonitorFixture : IDisposable
    {
        public DirectoryInfo Directory { get; } = System.IO.Directory.CreateTempSubdirectory("dbmcp-monitor-");
        public string Path => System.IO.Path.Combine(Directory.FullName, "databases.json");
        public MonitorConfigStub Config { get; }
        public DatabaseConfigFileMonitorService Monitor { get; }

        public MonitorFixture()
        {
            File.WriteAllText(Path, "initial");
            Config = new MonitorConfigStub { Path = Path };
            Monitor = new DatabaseConfigFileMonitorService(Config, NullLogger<DatabaseConfigFileMonitorService>.Instance);
        }

        public void Dispose()
        {
            Monitor.Dispose();
            Directory.Delete(true);
        }
    }

    private sealed class MonitorConfigStub : IDatabaseConfigService
    {
        private int _reloadCount;
        public string Path { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public int ReloadCount => Volatile.Read(ref _reloadCount);
        public int SettingsChecks { get; private set; }
        public bool FollowFileDefault { get; private set; }
        public Func<ConfigurationReloadResult> Reload { get; set; } = () => new() { Success = true };

        public ConfigurationReloadResult ReloadConfiguration(bool followFileDefault = false)
        {
            FollowFileDefault = followFileDefault;
            Interlocked.Increment(ref _reloadCount);
            return Reload();
        }

        public bool IsEnableMonitorConfigEnabled()
        {
            SettingsChecks++;
            return Enabled;
        }

        public string GetConfigFilePath() => Path;
        public DatabaseOperationReservation ReserveConnection(string? databaseName = null) => throw new NotSupportedException();
        public string GetConnectionString() => throw new NotSupportedException();
        public string GetDatabaseType() => throw new NotSupportedException();
        public bool ValidateConfiguration() => throw new NotSupportedException();
        public ConfigurationSummary GetConfigurationSummary() => throw new NotSupportedException();
        public List<DatabaseConnection> GetAllConnections() => throw new NotSupportedException();
        public bool SwitchDatabase(string databaseName) => throw new NotSupportedException();
        public string GetCurrentDatabaseName() => throw new NotSupportedException();
        public ConfigurationMutationResult RenameDatabase(string name, string newName) => throw new NotSupportedException();
        public ConfigurationMutationResult UpdateDatabase(string name, DatabaseConnectionUpdate update) => throw new NotSupportedException();
        public DatabaseRuntimeStatus GetRuntimeStatus() => throw new NotSupportedException();
    }
}
