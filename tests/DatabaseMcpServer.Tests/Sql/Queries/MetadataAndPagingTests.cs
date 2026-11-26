using System.Text.Json;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using DatabaseMcpServer.Tools.Command;
using DatabaseMcpServer.Tools.Management;
using DatabaseMcpServer.Tools.Query;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseMcpServer.Tests;

public class MetadataAndPagingTests
{
    [Theory]
    [InlineData("nullable-text", false)]
    [InlineData("nullable-composite", false)]
    [InlineData("integer-desc", false)]
    [InlineData("int-primary", false)]
    [InlineData("integer-alias", true)]
    [InlineData("not-null-text", true)]
    [InlineData("without-rowid", true)]
    [InlineData("strict", true)]
    public async Task SqlitePrimaryKeys_ReportNullabilityAndRejectUnsafePaging(string scenario, bool pageable)
    {
        using var fixture = DatabaseRoleTests.Create();
        var definition = scenario switch
        {
            "nullable-text" => "k TEXT PRIMARY KEY, value TEXT",
            "nullable-composite" => "k TEXT, value TEXT NOT NULL, PRIMARY KEY(k,value)",
            "integer-desc" => "k INTEGER PRIMARY KEY DESC, value TEXT",
            "int-primary" => "k INT PRIMARY KEY, value TEXT",
            "integer-alias" => "k INTEGER PRIMARY KEY, value TEXT",
            "not-null-text" => "k TEXT PRIMARY KEY NOT NULL, value TEXT",
            _ => "k TEXT PRIMARY KEY, value TEXT"
        };
        var suffix = scenario switch { "without-rowid" => " WITHOUT ROWID", "strict" => " STRICT", _ => "" };
        using (var setup = fixture.Sessions.AcquireClient())
        {
            setup.Client.Ado.ExecuteCommand($"CREATE TABLE paging_keys({definition}){suffix}");
            setup.Client.Ado.ExecuteCommand(pageable
                ? "INSERT INTO paging_keys VALUES(1,'first'),(2,'second')"
                : "INSERT INTO paging_keys VALUES(NULL,'first'),(2,'second')");
        }
        var schema = DatabaseRoleTests.Success(await fixture.Host.Services.GetRequiredService<SchemaTools>()
            .GetTableSchema("paging_keys", sections: ["columns"]));
        var column = schema.GetProperty("data").GetProperty("columns").EnumerateArray()
            .Single(c => c.GetProperty("name").GetString() == "k");
        Assert.Equal(!pageable, column.GetProperty("nullable").GetBoolean());
        Assert.Equal(scenario == "integer-alias", column.GetProperty("identity").GetBoolean());

        var queries = fixture.Host.Services.GetRequiredService<QueryTools>();
        var first = await queries.QueryTablePage("paging_keys", pageSize: 1);
        if (!pageable)
        {
            DatabaseRoleTests.Error(first, 1004);
            Assert.Contains("非空", first.StructuredContent!.Value.GetProperty("errorMessage").GetString());
            return;
        }
        var page = DatabaseRoleTests.Success(first);
        Assert.Equal("first", Assert.Single(page.GetProperty("data").EnumerateArray()).GetProperty("value").GetString());
        var next = DatabaseRoleTests.Success(await queries.QueryTablePage("paging_keys", pageSize: 1,
            cursor: page.GetProperty("nextCursor").GetString()));
        Assert.Equal("second", Assert.Single(next.GetProperty("data").EnumerateArray()).GetProperty("value").GetString());
    }

    [Fact]
    public async Task SchemaCache_IsolatesTargetsRefreshesAndInvalidatesAfterDdl()
    {
        using var fixture = DatabaseRoleTests.Create();
        var schema = fixture.Host.Services.GetRequiredService<SchemaTools>();
        var commands = fixture.Host.Services.GetRequiredService<CommandTools>();
        async Task<JsonElement> Schema(string db, bool refresh = false) => DatabaseRoleTests.Success(await schema.GetTableSchema("markers", db, refresh)).GetProperty("data");
        Assert.Single((await Schema("analytics")).GetProperty("columns").EnumerateArray());
        using (var external = fixture.Sessions.AcquireClient("analytics"))
            external.Client.Ado.ExecuteCommand("alter table markers add label text");
        Assert.Single((await Schema("analytics")).GetProperty("columns").EnumerateArray());
        Assert.Equal(2, (await Schema("analytics", true)).GetProperty("columns").GetArrayLength());
        Assert.Single((await Schema("primary")).GetProperty("columns").EnumerateArray());
        DatabaseRoleTests.Success(await commands.ExecuteCommand("alter table markers add stamp integer", databaseName: "analytics"));
        Assert.Equal(3, (await Schema("analytics")).GetProperty("columns").GetArrayLength());
        DatabaseRoleTests.Success(schema.AddColumn("markers", new() { Name = "memo", DataType = "text", Nullable = true }, "analytics"));
        Assert.Equal(4, (await Schema("analytics")).GetProperty("columns").GetArrayLength());
    }

    [Fact]
    public async Task MetadataCache_CoalescesLoadsExpiresBoundsCapacityAndDoesNotResurrectInvalidation()
    {
        var clock = new ManualTime();
        var cache = new DatabaseMetadataCache(new() { MetadataCacheSeconds = 30, MetadataCacheEntries = 2 }, clock);
        using var fixture = DatabaseRoleTests.Create();
        using var lease = fixture.Sessions.AcquireClient();
        var context = lease.Context;
        var count = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<JsonElement> Load() { Interlocked.Increment(ref count); await release.Task.WaitAsync(TimeSpan.FromSeconds(5)); return JsonSerializer.SerializeToElement(count); }
        var first = cache.GetAsync(context, "a", false, Load, default);
        var second = cache.GetAsync(context, "a", false, Load, default);
        release.SetResult();
        Assert.Equal(1, (await first).GetInt32());
        Assert.Equal(1, (await second).GetInt32());
        Assert.Equal(1, count);
        clock.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(2, (await cache.GetAsync(context, "a", false, Load, default)).GetInt32());
        await cache.GetAsync(context, "b", false, Load, default);
        clock.Advance(TimeSpan.FromSeconds(1));
        await cache.GetAsync(context, "c", false, Load, default);
        await cache.GetAsync(context, "a", false, Load, default);
        Assert.Equal(5, count);
        cache.Invalidate(context);
        await cache.GetAsync(context, "a", false, Load, default);
        Assert.Equal(6, count);
        await cache.GetAsync(context with { ConfigurationVersion = context.ConfigurationVersion + 1 }, "a", false, Load, default);
        Assert.Equal(7, count);
        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetAsync(context, "failed", false, () => throw new InvalidOperationException(), default));
        Assert.Equal(8, (await cache.GetAsync(context, "failed", false, Load, default)).GetInt32());
        release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inflight = cache.GetAsync(context, "inflight", false, Load, default);
        cache.Invalidate(context);
        release.SetResult();
        await inflight;
        Assert.Equal(10, (await cache.GetAsync(context, "inflight", false, Load, default)).GetInt32());
        var renamed = context with { DatabaseName = "renamed", ConfigurationVersion = context.ConfigurationVersion + 1 };
        await cache.GetAsync(renamed, "after-rename", false, Load, default);
        cache.Invalidate(context); // an older write completes after the connection was renamed
        Assert.Equal(12, (await cache.GetAsync(renamed, "after-rename", false, Load, default)).GetInt32());
    }

    [Fact]
    public async Task TablePagination_UsesUniqueOrderRejectsMissingKeyAndAvoidsImplicitCount()
    {
        using var fixture = DatabaseRoleTests.Create();
        using (var lease = fixture.Sessions.AcquireClient("analytics"))
        {
            lease.Client.Ado.ExecuteCommand("insert into markers values (9),(3),(7),(1); create table no_key(v int)");
        }
        var queries = fixture.Host.Services.GetRequiredService<QueryTools>();
        var rows = new List<int>();
        for (var page = 1; page <= 3; page++)
        {
            var result = DatabaseRoleTests.Success(await queries.QueryTablePage("markers", page, 2, "analytics"));
            Assert.Equal("analytics", result.GetProperty("databaseName").GetString());
            Assert.Equal("value", Assert.Single(result.GetProperty("orderBy").EnumerateArray()).GetString());
            Assert.False(result.TryGetProperty("totalCount", out _));
            rows.AddRange(result.GetProperty("data").EnumerateArray().Select(row => row.GetProperty("value").GetInt32()));
            Assert.Equal(page < 3, result.GetProperty("mayHaveMore").GetBoolean());
        }
        Assert.Equal(new[] { 1, 2, 3, 7, 9 }, rows);
        DatabaseRoleTests.Error(await queries.QueryTablePage("no_key", databaseName: "analytics"), 1004);
        DatabaseRoleTests.Error(await queries.QueryTablePage("markers;drop table markers"), 1004);
        DatabaseRoleTests.Error(await queries.QueryTablePage("markers", page: 0), 1004);
    }

    [Fact]
    public async Task CapabilityDiagnostics_ReportActualDriverOptionsAndRedactSecrets()
    {
        var builder = new MySqlConnector.MySqlConnectionStringBuilder
        { Server = "secret-host", UserID = "secret-user", Password = "secret-password", MaximumPoolSize = 17, SslMode = MySqlConnector.MySqlSslMode.Required };
        var options = JsonSerializer.SerializeToElement(DatabaseCapabilities.DriverOptions(SqlSugar.DbType.MySql, builder.ConnectionString));
        Assert.Equal(17u, options.GetProperty("maxPoolSize").GetUInt32());
        Assert.Equal("Required", options.GetProperty("sslMode").GetString());
        Assert.DoesNotContain("secret", options.GetRawText());
        var invalid = JsonSerializer.SerializeToElement(DatabaseCapabilities.DriverOptions(SqlSugar.DbType.MySql, "Password=secret;MaximumPoolSize=invalid"));
        Assert.Equal("invalid", invalid.GetProperty("status").GetString());
        Assert.DoesNotContain("secret", invalid.GetRawText());
        using var fixture = DatabaseRoleTests.Create();
        var result = DatabaseRoleTests.Success(await fixture.Host.Services.GetRequiredService<ConnectionTools>().GetDatabaseCapabilities("analytics"));
        Assert.Equal("supported", result.GetProperty("capabilities").GetProperty("boundedMultipleResults").GetString());
        Assert.Equal("unsupported", result.GetProperty("capabilities").GetProperty("typedProcedureOutput").GetString());
        Assert.Equal("analytics", result.GetProperty("databaseName").GetString());
        using var fakeClient = new SqlSugar.SqlSugarClient(new SqlSugar.ConnectionConfig { DbType = SqlSugar.DbType.MySql, ConnectionString = builder.ConnectionString,
            MoreSettings = new SqlSugar.ConnMoreSettings { DisableNvarchar = false } });
        var diagnostic = JsonSerializer.SerializeToElement(DatabaseCapabilities.Describe(new DatabaseMcpServer.Models.DatabaseOperationContext(new(), "diagnostic")
        {
            DatabaseType = "MySql",
            OptimizationSettings = new Dictionary<string, string> { ["maxPoolSize"] = "999", ["enableSsl"] = "false", ["disableNvarchar"] = "secret-password", ["unknown"] = "secret-password" }
        }, new(), fakeClient.CurrentConnectionConfig));
        var settings = diagnostic.GetProperty("optimizationSettings").EnumerateArray().ToDictionary(e => e.GetProperty("name").GetString()!);
        Assert.Equal("advisory", settings["maxPoolSize"].GetProperty("status").GetString());
        Assert.Equal(17u, diagnostic.GetProperty("connectionOptions").GetProperty("maxPoolSize").GetUInt32());
        Assert.Equal("Required", diagnostic.GetProperty("connectionOptions").GetProperty("sslMode").GetString());
        Assert.Equal("ignored-invalid", settings["disableNvarchar"].GetProperty("status").GetString());
        Assert.Equal("[redacted]", settings["disableNvarchar"].GetProperty("configured").GetString());
        Assert.DoesNotContain("secret-password", diagnostic.GetRawText());
    }

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan span) => _now += span;
    }
}
