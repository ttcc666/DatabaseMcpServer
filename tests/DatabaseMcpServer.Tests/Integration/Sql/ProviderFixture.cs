using System.Diagnostics;
using System.Text.Json;
using DatabaseMcpServer.Extensions;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Tools.Command;
using DatabaseMcpServer.Tools.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Tests.Integration;

public sealed class ProviderTheoryAttribute : TheoryAttribute
{
    public ProviderTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("DBMCP_RUN_INTEGRATION") != "1")
            Skip = "Run scripts/test-integration.ps1 to enable isolated database tests.";
    }
}

internal sealed class ProviderFixture : IDisposable
{
    private readonly DirectoryInfo _directory;
    private readonly IHost _host;
    public string Provider { get; }
    public SqlSugarClient Observer { get; }
    public QueryTools Query => _host.Services.GetRequiredService<QueryTools>();
    public CommandTools Command => _host.Services.GetRequiredService<CommandTools>();
    public DatabaseQueryTools StructuredQuery => _host.Services.GetRequiredService<DatabaseQueryTools>();

    public ProviderFixture(string provider)
    {
        Provider = provider;
        var connectionString = Environment.GetEnvironmentVariable("DBMCP_TEST_" + provider.ToUpperInvariant());
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException($"Integration enabled but DBMCP_TEST_{provider.ToUpperInvariant()} is missing.");
        _directory = Directory.CreateTempSubdirectory("dbmcp-provider-");
        try
        {
            var config = Path.Combine(_directory.FullName, "databases.json");
            File.WriteAllText(config, JsonSerializer.Serialize(new
            {
                databases = new[] { new { name = "integration", dbType = provider, connectionString, isDefault = true, enableDangerousOperations = true } }
            }));
            var builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Services.AddDatabaseMcpApplicationServices(true, Path.Combine(_directory.FullName, "state.json"), false, config);
            // A single slot makes the successful follow-up also prove admission was released.
            builder.Services.AddSingleton(new DatabaseExecutionOptions { MaxConcurrent = 1, MaxQueued = 1, QueueTimeoutSeconds = 2 });
            builder.Services.AddDatabaseMcpToolServices();
            _host = builder.Build();
            Observer = new SqlSugarClient(new ConnectionConfig
            {
                DbType = Enum.Parse<SqlSugar.DbType>(provider), ConnectionString = connectionString,
                IsAutoCloseConnection = true
            });
            Observer.Ado.CommandTimeOut = 3;
        }
        catch { _directory.Delete(true); throw; }
    }

    public string SlowCommand(string marker) => $"/*{marker}*/ " + (Provider switch
    {
        // Standalone MySQL SLEEP returns 1 on interruption, which is a successful statement.
        "MySql" => "SELECT SLEEP(30) + 1",
        "PostgreSQL" => "SELECT 1 FROM pg_sleep(30)",
        "SqlServer" => "WAITFOR DELAY '00:00:30'",
        _ => throw new NotSupportedException()
    });

    public string SlowQuery(string marker) => Provider == "SqlServer"
        ? $"/*{marker}*/ SELECT COUNT_BIG(*) FROM sys.all_objects a CROSS JOIN sys.all_objects b CROSS JOIN sys.all_objects c"
        : SlowCommand(marker);

    public async Task WaitForActivity(string marker, bool active)
    {
        var sql = Provider switch
        {
            "MySql" => "SELECT COUNT(*) FROM information_schema.PROCESSLIST WHERE ID <> CONNECTION_ID() AND INFO LIKE @pattern",
            "PostgreSQL" => "SELECT COUNT(*) FROM pg_stat_activity WHERE pid <> pg_backend_pid() AND state = 'active' AND query LIKE @pattern",
            "SqlServer" => "SELECT COUNT(*) FROM sys.dm_exec_requests r CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) t WHERE r.session_id <> @@SPID AND t.text LIKE @pattern",
            _ => throw new NotSupportedException()
        };
        var watch = Stopwatch.StartNew();
        do
        {
            var count = Convert.ToInt32(await Observer.Ado.GetScalarAsync(sql, new SugarParameter("@pattern", "%" + marker + "%")));
            if ((count > 0) == active) return;
            await Task.Delay(100);
        } while (watch.Elapsed < TimeSpan.FromSeconds(10));
        Assert.Fail($"{Provider}: expected server activity={active} for the test query.");
    }

    public static JsonDocument Success(string payload)
    {
        var document = JsonDocument.Parse(payload);
        if (!document.RootElement.GetProperty("success").GetBoolean())
        {
            document.Dispose();
            Assert.Fail(payload);
        }
        return document;
    }

    public async Task AssertUsable()
    {
        using var result = Success(await Query.GetScalar("SELECT 17"));
        Assert.Equal(17, result.RootElement.GetProperty("value").GetInt32());
    }

    public void Dispose()
    {
        Observer.Dispose();
        _host.Dispose();
        _directory.Delete(true);
    }
}
