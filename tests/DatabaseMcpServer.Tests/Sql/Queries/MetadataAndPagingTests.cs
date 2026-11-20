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
    [Fact]
    public async Task SchemaCache_IsolatesTargetsRefreshesAndInvalidatesAfterDdl()
    {
        using var fixture = DatabaseRoleTests.Create();
        var schema = fixture.Host.Services.GetRequiredService<SchemaTools>();
        var commands = fixture.Host.Services.GetRequiredService<CommandTools>();
        JsonElement Schema(string db, bool refresh = false) => DatabaseRoleTests.Success(schema.GetTableSchema("markers", db, refresh)).GetProperty("data");
        Assert.Single(Schema("analytics").GetProperty("columns").EnumerateArray());
        using (var external = fixture.Sessions.AcquireClient("analytics"))
            external.Client.Ado.ExecuteCommand("alter table markers add label text");
        Assert.Single(Schema("analytics").GetProperty("columns").EnumerateArray());
        Assert.Equal(2, Schema("analytics", true).GetProperty("columns").GetArrayLength());
        Assert.Single(Schema("primary").GetProperty("columns").EnumerateArray());
        DatabaseRoleTests.Success(await commands.ExecuteCommand("alter table markers add stamp integer", databaseName: "analytics"));
        Assert.Equal(3, Schema("analytics").GetProperty("columns").GetArrayLength());
        DatabaseRoleTests.Success(schema.AddColumn("markers", """{"DbColumnName":"memo","DataType":"text","IsNullable":true}""", "analytics"));
        Assert.Equal(4, Schema("analytics").GetProperty("columns").GetArrayLength());
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
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        JsonElement Load() { Interlocked.Increment(ref count); entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(5))); return JsonSerializer.SerializeToElement(count); }
        var first = Task.Run(() => cache.Get(context, "a", false, Load));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        var second = Task.Run(() => cache.Get(context, "a", false, Load));
        release.Set();
        Assert.Equal(1, (await first).GetInt32());
        Assert.Equal(1, (await second).GetInt32());
        Assert.Equal(1, count);
        clock.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(2, cache.Get(context, "a", false, Load).GetInt32());
        cache.Get(context, "b", false, Load);
        clock.Advance(TimeSpan.FromSeconds(1));
        cache.Get(context, "c", false, Load);
        cache.Get(context, "a", false, Load);
        Assert.Equal(5, count);
        cache.Invalidate(context);
        cache.Get(context, "a", false, Load);
        Assert.Equal(6, count);
        cache.Get(context with { ConfigurationVersion = context.ConfigurationVersion + 1 }, "a", false, Load);
        Assert.Equal(7, count);
        Assert.Throws<InvalidOperationException>(() => cache.Get(context, "failed", false, () => throw new InvalidOperationException()));
        Assert.Equal(8, cache.Get(context, "failed", false, Load).GetInt32());
        entered.Reset(); release.Reset();
        var inflight = Task.Run(() => cache.Get(context, "inflight", false, Load));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        cache.Invalidate(context);
        release.Set();
        await inflight;
        Assert.Equal(10, cache.Get(context, "inflight", false, Load).GetInt32());
        var renamed = context with { DatabaseName = "renamed", ConfigurationVersion = context.ConfigurationVersion + 1 };
        cache.Get(renamed, "after-rename", false, Load);
        cache.Invalidate(context); // an older write completes after the connection was renamed
        Assert.Equal(12, cache.Get(renamed, "after-rename", false, Load).GetInt32());
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
    public void CapabilityDiagnostics_ReportActualDriverOptionsAndRedactSecrets()
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
        var result = DatabaseRoleTests.Success(fixture.Host.Services.GetRequiredService<ConnectionTools>().GetDatabaseCapabilities("analytics"));
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
