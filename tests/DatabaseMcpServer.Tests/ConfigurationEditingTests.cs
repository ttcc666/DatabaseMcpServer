using System.Text.Json;
using DatabaseMcpServer.Cli;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using DatabaseMcpServer.Web;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseMcpServer.Tests;

public class ConfigurationEditingTests
{
    [Fact]
    public void CombinedEdit_ValidatesBeforeRenameAndPublishesTogether()
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        var original = File.ReadAllText(fixture.Path);
        var invalid = fixture.Service.UpdateDatabase("primary", new()
        { NewName = "renamed", ApplyDbType = true, DbType = "unsupported" });
        Assert.False(invalid.Saved);
        Assert.Equal(original, File.ReadAllText(fixture.Path));
        Assert.Equal("primary", fixture.Service.GetCurrentDatabaseName());
        var valid = fixture.Service.UpdateDatabase("primary", new()
        { NewName = "renamed", ApplyDescription = true, Description = "updated" });
        Assert.True(valid.Success, valid.Message);
        Assert.True(valid.RuntimeApplied);
        Assert.Equal("renamed", valid.DatabaseName);
        Assert.Equal("renamed", fixture.Service.GetCurrentDatabaseName());
        var saved = new CliConfigFileService().Load(fixture.Path);
        Assert.Equal("updated", Assert.Single(saved.Databases, db => db.Name == "renamed").Description);
        Assert.DoesNotContain(saved.Databases, db => db.Name == "primary");
    }

    [Fact]
    public void PartialEdit_ReportsSavedIdentityAndCanBeRetried()
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        File.WriteAllText(fixture.StatePath, "{");
        var result = fixture.Service.UpdateDatabase("primary", new()
        { NewName = "renamed", ApplyDescription = true, Description = "saved" });
        Assert.False(result.Success);
        Assert.True(result.Saved);
        Assert.True(result.RuntimeApplied);
        Assert.Equal("renamed", result.DatabaseName);
        var retry = fixture.Service.UpdateDatabase("renamed", new() { ApplyDescription = true, Description = "retry" });
        Assert.True(retry.Success, retry.Message);
        Assert.Equal("retry", fixture.Service.GetAllConnections().Single(db => db.Name == "renamed").Description);
    }

    [Fact]
    public void Dashboard_ReportsActiveSelectionAndUnappliedChanges()
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        var api = new CliWebApiService(new(fixture.Path, "test"), new(), new(),
            fixture.Host.Services.GetRequiredService<ICurrentDatabaseStateStore>(), new(), fixture.Host.Services);
        var before = fixture.Service.GetRuntimeStatus();
        var saved = new CliConfigFileService().Load(fixture.Path);
        saved.Databases[0].Name = "external";
        File.WriteAllText(fixture.Path, JsonSerializer.Serialize(saved));
        var dashboard = JsonSerializer.SerializeToElement(api.GetDashboard());
        Assert.Equal("primary", dashboard.GetProperty("currentDatabase").GetString());
        var status = fixture.Service.GetRuntimeStatus();
        Assert.True(status.HasPendingChanges);
        Assert.Equal(before.RuntimeVersion, status.RuntimeVersion);
        Assert.True(status.SavedVersion > status.RuntimeVersion);
        Assert.True(fixture.Service.ReloadConfiguration().Success);
        Assert.False(fixture.Service.GetRuntimeStatus().HasPendingChanges);
        Assert.Equal("external", fixture.Service.GetCurrentDatabaseName());
    }

    [Fact]
    public async Task Diagnostics_KeepProcessWritersAndUseSavedCandidate()
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        var stdout = Console.Out;
        var stderr = Console.Error;
        var handler = new CliConfigCommandHandler(new JsonResultSerializer(), new(),
            fixture.Host.Services.GetRequiredService<ISqlSugarClientFactory>());
        using var result = JsonDocument.Parse(await handler.DoctorAsync(fixture.Path, null, true, false, false));
        Assert.True(result.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(2, result.RootElement.GetProperty("testedConnections").GetInt32());
        Assert.Same(stdout, Console.Out);
        Assert.Same(stderr, Console.Error);
        // The saved candidate differs from the published runtime. Diagnostics must not reuse
        // the healthy runtime client and report success for an invalid saved connection.
        var saved = new CliConfigFileService().Load(fixture.Path);
        saved.Databases[0].ConnectionString = $"Data Source={Path.Combine(fixture.Directory.FullName, "missing", "db.sqlite")};Pooling=False";
        File.WriteAllText(fixture.Path, JsonSerializer.Serialize(saved));
        using var failed = JsonDocument.Parse(await handler.TestAsync(fixture.Path, "primary"));
        Assert.False(failed.RootElement.GetProperty("success").GetBoolean());
        using var active = fixture.Service.AcquireClient();
        Assert.Equal(29, active.Client.Ado.GetInt("select 29"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Dashboard_KeepsActiveTargetWhenSavedFileIsUnavailable(bool missing)
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        var api = new CliWebApiService(new(fixture.Path, "test"), new(), new(),
            fixture.Host.Services.GetRequiredService<ICurrentDatabaseStateStore>(), new(), fixture.Host.Services);
        if (missing) File.Delete(fixture.Path);
        else File.WriteAllText(fixture.Path, "{");
        var dashboard = JsonSerializer.SerializeToElement(api.GetDashboard());
        Assert.Equal("primary", dashboard.GetProperty("currentDatabase").GetString());
        Assert.True(fixture.Service.GetRuntimeStatus().HasPendingChanges);
        using var lease = fixture.Service.AcquireClient();
        Assert.Equal(5, lease.Client.Ado.GetInt("select 5"));
    }
}
