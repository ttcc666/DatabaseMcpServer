using System.Text.Json;
using DatabaseMcpServer.Tools.Management;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseMcpServer.Tests;

public class ObjectMetadataTests
{
    [Fact]
    public async Task NativeSections_ShareVisibilityProbeAndDoNotCacheFailures()
    {
        var commands = new List<string>();
        var fail = false;
        using var fixture = new RuntimeConsistencyTests.Fixture(client => client.Aop.OnLogExecuting = (sql, _) =>
        {
            commands.Add(sql);
            if (fail && sql.Contains("pragma_foreign_key_list", StringComparison.Ordinal)) throw new InvalidOperationException("Transient metadata failure");
        });
        var path = Path.Combine(fixture.Directory.FullName, "metadata.db");
        Assert.True(fixture.Service.UpdateDatabase("primary", new() { ApplyConnectionString = true, ConnectionString = $"Data Source={path};Pooling=False" }).Success);
        using (var seed = fixture.Sessions.AcquireClient()) seed.Client.Ado.ExecuteCommand("CREATE TABLE \"odd's table\"(a int,b int,PRIMARY KEY(b,a))");
        var tool = fixture.Host.Services.GetRequiredService<SchemaTools>();
        commands.Clear();
        var result = DatabaseRoleTests.Success(await tool.GetTableSchema("odd's table", sections: ["foreignKeys", "constraints"])).GetProperty("data");
        Assert.Equal("available", result.GetProperty("foreignKeys").GetProperty("status").GetString());
        Assert.Equal(3, commands.Count); // One visibility query and one query per requested native section.
        await tool.GetTableSchema("odd's table", sections: ["constraints", "foreignKeys", "constraints"]);
        Assert.Equal(3, commands.Count); // Normalization hits the same cache entry.
        fail = true;
        var failed = DatabaseRoleTests.Success(await tool.GetTableSchema("odd's table", refresh: true, sections: ["foreignKeys"])).GetProperty("data");
        Assert.Equal("unavailable", failed.GetProperty("foreignKeys").GetProperty("status").GetString());
        fail = false;
        var recovered = DatabaseRoleTests.Success(await tool.GetTableSchema("odd's table", sections: ["foreignKeys"])).GetProperty("data");
        Assert.Equal("available", recovered.GetProperty("foreignKeys").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Objects_PageLiteralPrefixesAndBindCursorToFiltersAndVersion()
    {
        using var fixture = DatabaseRoleTests.Create();
        using (var db = fixture.Sessions.AcquireClient())
            db.Client.Ado.ExecuteCommand("CREATE TABLE \"t_%a\"(id int); CREATE TABLE \"t_%b\"(id int); CREATE TABLE t_other(id int)");
        var tools = fixture.Host.Services.GetRequiredService<ObjectMetadataTools>();
        var first = DatabaseRoleTests.Success(await tools.ListObjects("table", namePrefix: "t_%", limit: 1).Json());
        Assert.Equal("t_%a", first.GetProperty("objects")[0].GetProperty("name").GetString());
        var cursor = first.GetProperty("nextCursor").GetString();
        Assert.NotNull(cursor);
        var next = DatabaseRoleTests.Success(await tools.ListObjects("table", namePrefix: "t_%", limit: 1, cursor: cursor).Json());
        Assert.Equal("t_%b", next.GetProperty("objects")[0].GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, next.GetProperty("nextCursor").ValueKind);
        DatabaseRoleTests.Error(await tools.ListObjects("table", namePrefix: "t_", limit: 1, cursor: cursor).Json(), 1004);
        DatabaseRoleTests.Error(await tools.ListObjects("table", databaseName: "analytics", namePrefix: "t_%", limit: 1, cursor: cursor).Json(), 1004);
        File.WriteAllText(fixture.Path, File.ReadAllText(fixture.Path).Replace("Pooling=False", "Pooling=False;Default Timeout=20", StringComparison.Ordinal));
        fixture.Service.ReloadConfiguration();
        DatabaseRoleTests.Error(await tools.ListObjects("table", namePrefix: "t_%", limit: 1, cursor: cursor).Json(), 1004);
    }

    [Fact]
    public async Task Definition_HandlesQuotedNamesUnavailableAndBudget()
    {
        using var fixture = DatabaseRoleTests.Create();
        using (var db = fixture.Sessions.AcquireClient())
            db.Client.Ado.ExecuteCommand("CREATE VIEW \"select's view\" AS SELECT '" + new string('a', 3000) + "' AS value");
        var tools = fixture.Host.Services.GetRequiredService<ObjectMetadataTools>();
        var result = DatabaseRoleTests.Success(await tools.GetObjectDefinition("view", "select's view").Json());
        Assert.Equal("available", result.GetProperty("availability").GetString());
        Assert.Contains("CREATE VIEW", result.GetProperty("definition").GetString());
        DatabaseRoleTests.Error(await tools.GetObjectDefinition("view", "select's view", maxResponseBytes: 1024).Json(), 1013);
        var missing = DatabaseRoleTests.Success(await tools.GetObjectDefinition("view", "missing").Json());
        Assert.Equal("definition-unavailable", missing.GetProperty("availability").GetString());
    }

    [Fact]
    public async Task Sections_PreserveCompositeOrderAndSeparateCacheEntries()
    {
        using var fixture = DatabaseRoleTests.Create();
        using (var db = fixture.Sessions.AcquireClient())
            db.Client.Ado.ExecuteCommand("CREATE TABLE parent(a int,b int,PRIMARY KEY(b,a)); CREATE TABLE child(x int,y int,PRIMARY KEY(y,x),FOREIGN KEY(y,x) REFERENCES parent(b,a) ON DELETE CASCADE)");
        var tools = fixture.Host.Services.GetRequiredService<SchemaTools>();
        var columns = DatabaseRoleTests.Success(await tools.GetTableSchema("child", sections: ["columns"])).GetProperty("data");
        Assert.False(columns.TryGetProperty("indexes", out _));
        var data = DatabaseRoleTests.Success(await tools.GetTableSchema("child", sections: ["foreignKeys", "constraints", "comments"])).GetProperty("data");
        Assert.False(data.TryGetProperty("columns", out _));
        Assert.Equal("available", data.GetProperty("foreignKeys").GetProperty("status").GetString());
        var fk = data.GetProperty("foreignKeys").GetProperty("data")[0];
        Assert.Equal(new[] { "y", "x" }, fk.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("sourceColumn").GetString()));
        Assert.Equal(new[] { "b", "a" }, fk.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("targetColumn").GetString()));
        Assert.Equal("CASCADE", fk.GetProperty("onDelete").GetString());
        Assert.Equal("partial", data.GetProperty("constraints").GetProperty("status").GetString());
        Assert.Equal(new[] { "y", "x" }, data.GetProperty("constraints").GetProperty("data")[0].GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("columnName").GetString()));
        Assert.Equal("unsupported", data.GetProperty("comments").GetProperty("status").GetString());
        DatabaseRoleTests.Error(await tools.GetTableSchema("child", sections: ["typo"]), 1004);
    }
}
