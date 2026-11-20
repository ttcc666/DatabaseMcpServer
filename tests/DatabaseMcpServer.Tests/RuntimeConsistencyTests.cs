using System.Text.Json;
using DatabaseMcpServer.Cli;
using DatabaseMcpServer.Extensions;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DatabaseMcpServer.Tests;

public class RuntimeConsistencyTests
{
    [Fact]
    public void Hosts_KeepInjectedPathsAcrossEnvironmentChanges()
    {
        using var first = new Fixture();
        using var second = new Fixture();
        var previous = Environment.GetEnvironmentVariable("DB_CONFIG_PATH");
        try
        {
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", second.Path);
            Assert.True(first.Service.SwitchDatabase("analytics"));
            Assert.True(first.Service.ReloadConfiguration().Success);
            Assert.Equal(first.Path, first.Service.GetConfigFilePath());
            Assert.Equal("analytics", first.Service.GetCurrentDatabaseName());
            Assert.Equal("primary", second.Service.GetCurrentDatabaseName());
        }
        finally { Environment.SetEnvironmentVariable("DB_CONFIG_PATH", previous); }
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("defaults")]
    [InlineData("type")]
    [InlineData("null")]
    [InlineData("null-list")]
    [InlineData("name")]
    [InlineData("connection")]
    public void InvalidConfig_IsRejectedByImportValidateAndReload(string defect)
    {
        using var fixture = new Fixture();
        var original = File.ReadAllText(fixture.Path);
        var config = new CliConfigFileService().Load(fixture.Path);
        switch (defect)
        {
            case "duplicate": config.Databases[1].Name = "primary"; break;
            case "defaults": config.Databases[1].IsDefault = true; break;
            case "type": config.Databases[1].DbType = "invalid"; break;
            case "null": config.Databases.Add(null!); break;
            case "null-list": config.Databases = null!; break;
            case "name": config.Databases[1].Name = ""; break;
            case "connection": config.Databases[1].ConnectionString = ""; break;
        }
        var input = System.IO.Path.Combine(fixture.Directory.FullName, "invalid.json");
        File.WriteAllText(input, JsonSerializer.Serialize(config));
        var handler = new CliConfigCommandHandler();
        using var validation = JsonDocument.Parse(handler.Validate(input));
        Assert.False(validation.RootElement.GetProperty("success").GetBoolean());
        using var import = JsonDocument.Parse(handler.Import(fixture.Path, input, true));
        Assert.False(import.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(original, File.ReadAllText(fixture.Path));
        File.Copy(input, fixture.Path, true);
        Assert.False(fixture.Service.ReloadConfiguration().Success);
        Assert.Equal(new[] { "primary", "analytics" }, fixture.Service.GetAllConnections().Select(db => db.Name));
    }

    [Fact]
    public async Task Doctor_ValidatesWholeDocumentBeforeSelectingConnection()
    {
        using var fixture = new Fixture();
        var config = new CliConfigFileService().Load(fixture.Path);
        config.Databases[1].DbType = "unsupported";
        File.WriteAllText(fixture.Path, JsonSerializer.Serialize(config));
        var payload = await new CliConfigCommandHandler().DoctorAsync(fixture.Path, "primary", false, false, false);
        using var result = JsonDocument.Parse(payload);
        Assert.False(result.RootElement.GetProperty("success").GetBoolean());
        Assert.NotEmpty(result.RootElement.GetProperty("configErrors").EnumerateArray());
        Assert.Equal(0, result.RootElement.GetProperty("testedConnections").GetInt32());
    }

    [Theory]
    [InlineData("analytics", "analytics", "reporting")]
    [InlineData("primary", "analytics", "primary")]
    [InlineData("primary", "primary", "reporting")]
    public void Rename_PreservesSelectionAcrossReloadAndRestart(string current, string renamed, string expected)
    {
        using var fixture = new Fixture();
        Assert.True(fixture.Service.SwitchDatabase(current));
        var result = fixture.Service.RenameDatabase(renamed, "reporting");
        Assert.True(result.Success, result.Message);
        Assert.True(result.Saved);
        Assert.True(result.RuntimeApplied);
        Assert.Equal(expected, fixture.Service.GetCurrentDatabaseName());
        Assert.True(fixture.Service.ReloadConfiguration(true).Success);
        Assert.Equal(expected, fixture.Service.GetCurrentDatabaseName());
        using var restarted = fixture.BuildHost();
        Assert.Equal(expected, restarted.Services.GetRequiredService<IDatabaseConfigService>().GetCurrentDatabaseName());
    }

    [Fact]
    public void Rename_DoesNotOverwriteAnotherProcessSelection()
    {
        using var fixture = new Fixture();
        fixture.Service.SwitchDatabase("analytics");
        var store = fixture.Host.Services.GetRequiredService<ICurrentDatabaseStateStore>();
        store.SaveRequiredCurrentDatabaseName(fixture.Path, "primary");
        var result = fixture.Service.RenameDatabase("analytics", "renamed");
        Assert.True(result.RuntimeApplied);
        Assert.False(result.SelectionPersisted);
        fixture.Service.ReloadConfiguration(true);
        Assert.Equal("primary", store.GetCurrentDatabaseName(fixture.Path));
        Assert.Equal("renamed", fixture.Service.GetCurrentDatabaseName());
    }

    [Fact]
    public void WebSave_ReportsReloadFailureAndKeepsLastValidSnapshot()
    {
        using var fixture = new Fixture();
        var reads = 0;
        var service = new DatabaseMcpServer.Services.DatabaseConfigService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DatabaseMcpServer.Services.DatabaseConfigService>.Instance,
            fixture.Host.Services.GetRequiredService<IDatabaseHelperService>(),
            fixture.Host.Services.GetRequiredService<ISqlSugarClientFactory>(),
            fixture.Host.Services.GetRequiredService<IJsonResultSerializer>(),
            fixture.Host.Services.GetRequiredService<ICurrentDatabaseStateStore>(),
            new DatabaseRuntimeOptions(false, fixture.Path),
            readConfig: path => ++reads == 1 ? File.ReadAllText(path) : throw new IOException("simulated read failure"));
        using var provider = new ServiceCollection().AddSingleton<IDatabaseConfigService>(service).BuildServiceProvider();
        var api = new CliWebApiService(new(fixture.Path, "test"), new(), new(),
            fixture.Host.Services.GetRequiredService<ICurrentDatabaseStateStore>(), new(), provider);
        using var result = JsonDocument.Parse(api.CloneDatabase("primary", new("copy", false)));
        Assert.False(result.RootElement.GetProperty("success").GetBoolean());
        Assert.True(result.RootElement.GetProperty("saved").GetBoolean());
        Assert.False(result.RootElement.GetProperty("runtimeApplied").GetBoolean());
        Assert.Contains("已保存", result.RootElement.GetProperty("message").GetString());
        Assert.Contains("copy", File.ReadAllText(fixture.Path));
        Assert.DoesNotContain(service.GetAllConnections(), db => db.Name == "copy");
    }

    [Fact]
    public void RemovingLastConnection_ReportsSavedButNotApplied()
    {
        using var fixture = new Fixture();
        var api = new CliWebApiService(new(fixture.Path, "test"), new(), new(),
            fixture.Host.Services.GetRequiredService<ICurrentDatabaseStateStore>(), new(), fixture.Host.Services);
        using var first = JsonDocument.Parse(api.RemoveDatabase("primary"));
        Assert.True(first.RootElement.GetProperty("success").GetBoolean());
        using var last = JsonDocument.Parse(api.RemoveDatabase("analytics"));
        Assert.False(last.RootElement.GetProperty("success").GetBoolean());
        Assert.True(last.RootElement.GetProperty("saved").GetBoolean());
        Assert.False(last.RootElement.GetProperty("runtimeApplied").GetBoolean());
        Assert.Empty(new CliConfigFileService().Load(fixture.Path).Databases);
        Assert.Equal("analytics", fixture.Service.GetCurrentDatabaseName());
    }

    [Fact]
    public void Reload_ReportsAppliedButUnpersistedSelection()
    {
        using var fixture = new Fixture();
        var config = new CliConfigFileService().Load(fixture.Path);
        config.Databases[0].IsDefault = false;
        config.Databases[1].IsDefault = true;
        File.WriteAllText(fixture.Path, JsonSerializer.Serialize(config));
        File.WriteAllText(fixture.StatePath, "{");
        var result = fixture.Service.ReloadConfiguration(true);
        Assert.False(result.Success);
        Assert.True(result.RuntimeApplied);
        Assert.False(result.SelectionPersisted);
        Assert.Equal("analytics", fixture.Service.GetCurrentDatabaseName());
        Assert.Equal("{", File.ReadAllText(fixture.StatePath));
    }

    [Fact]
    public void Monitor_DeduplicatesThePublishedSnapshotRatherThanEarlierFileHash()
    {
        using var fixture = new Fixture();
        var original = File.ReadAllText(fixture.Path);
        var reads = 0;
        var service = new DatabaseMcpServer.Services.DatabaseConfigService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DatabaseMcpServer.Services.DatabaseConfigService>.Instance,
            fixture.Host.Services.GetRequiredService<IDatabaseHelperService>(),
            fixture.Host.Services.GetRequiredService<ISqlSugarClientFactory>(),
            fixture.Host.Services.GetRequiredService<IJsonResultSerializer>(),
            fixture.Host.Services.GetRequiredService<ICurrentDatabaseStateStore>(),
            new DatabaseRuntimeOptions(true, fixture.Path),
            readConfig: path => ++reads == 2 ? original : File.ReadAllText(path));
        var config = new CliConfigFileService().Load(fixture.Path);
        config.Databases[0].IsDefault = false;
        config.Databases[1].IsDefault = true;
        File.WriteAllText(fixture.Path, JsonSerializer.Serialize(config));
        using var monitor = new DatabaseMcpServer.Services.DatabaseConfigFileMonitorService(service,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DatabaseMcpServer.Services.DatabaseConfigFileMonitorService>.Instance);
        monitor.HandleMonitoredFileChangedForTests();
        Assert.Equal("primary", service.GetCurrentDatabaseName());
        monitor.HandleMonitoredFileChangedForTests();
        Assert.Equal("analytics", service.GetCurrentDatabaseName());
    }

    [Fact]
    public void CliRename_PreservesPersistedSelection()
    {
        using var fixture = new Fixture();
        fixture.Service.SwitchDatabase("analytics");
        using var result = JsonDocument.Parse(new CliConfigCommandHandler().Rename(fixture.Path, "analytics", "reporting", fixture.StatePath));
        Assert.True(result.RootElement.GetProperty("success").GetBoolean());
        using var next = fixture.BuildHost();
        Assert.Equal("reporting", next.Services.GetRequiredService<IDatabaseConfigService>().GetCurrentDatabaseName());
    }

    [Fact]
    public void Rename_ReportsConflictAndPersistenceFailureWithoutRollback()
    {
        using var fixture = new Fixture();
        var original = File.ReadAllText(fixture.Path);
        Assert.False(fixture.Service.RenameDatabase("analytics", "primary").Saved);
        Assert.Equal(original, File.ReadAllText(fixture.Path));
        fixture.Service.SwitchDatabase("analytics");
        File.WriteAllText(fixture.StatePath, "{");
        var result = fixture.Service.RenameDatabase("analytics", "renamed");
        Assert.False(result.Success);
        Assert.True(result.Saved);
        Assert.True(result.RuntimeApplied);
        Assert.False(result.SelectionPersisted);
        Assert.Equal("renamed", fixture.Service.GetCurrentDatabaseName());
        Assert.Contains("renamed", File.ReadAllText(fixture.Path));
        Assert.Equal("{", File.ReadAllText(fixture.StatePath));
    }

    internal sealed class Fixture : IDisposable
    {
        public DirectoryInfo Directory { get; } = System.IO.Directory.CreateTempSubdirectory("dbmcp-runtime-");
        public string Path => System.IO.Path.Combine(Directory.FullName, "databases.json");
        public string StatePath => System.IO.Path.Combine(Directory.FullName, "state.json");
        public IHost Host { get; }
        public IDatabaseConfigService Service => Host.Services.GetRequiredService<IDatabaseConfigService>();

        public Fixture(Action<SqlSugar.ISqlSugarClient>? observeClient = null, Action<IServiceCollection>? configureServices = null)
        {
            File.WriteAllText(Path, """
                {"databases":[
                  {"name":"primary","dbType":"Sqlite","connectionString":"Data Source=:memory:","isDefault":true},
                  {"name":"analytics","dbType":"Sqlite","connectionString":"Data Source=:memory:"}
                ]}
                """);
            Host = BuildHost(observeClient, configureServices);
            _ = Service;
        }

        public IHost BuildHost(Action<SqlSugar.ISqlSugarClient>? observeClient = null, Action<IServiceCollection>? configureServices = null)
        {
            var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
            builder.Services.AddDatabaseMcpApplicationServices(true, StatePath, false, Path);
            builder.Services.AddDatabaseMcpToolServices();
            if (observeClient != null) ObservedClientFactory.Register(builder.Services, observeClient);
            configureServices?.Invoke(builder.Services);
            return builder.Build();
        }

        public void Dispose()
        {
            Host.Dispose();
            Directory.Delete(true);
        }
    }
}
