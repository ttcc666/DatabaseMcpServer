using System.Text.Json;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Tools;
using DatabaseMcpServer.Tools.Command;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseMcpServer.Tests;

public class InsertRowTests
{
    [Fact]
    public async Task Insert_ReturnsExactIdentityAndPreservesDefaultsAndTriggers()
    {
        using var fixture = DatabaseRoleTests.Create();
        using (var db = fixture.Sessions.AcquireClient())
            db.Client.Ado.ExecuteCommand("""
                CREATE TABLE items(id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL UNIQUE, amount REAL, note TEXT, active INTEGER, status TEXT DEFAULT 'new');
                INSERT INTO items(id,name) VALUES(9007199254740992,'seed');
                CREATE TABLE audit(id INTEGER PRIMARY KEY AUTOINCREMENT, item_id INTEGER);
                CREATE TRIGGER inserted AFTER INSERT ON items BEGIN INSERT INTO audit(item_id) VALUES(new.id); END;
                """);
        var command = fixture.Host.Services.GetRequiredService<CommandTools>();
        var result = DatabaseRoleTests.Success(await command.InsertRow("items", JsonSerializer.SerializeToElement(new { name = "O'Brien", amount = 10.5, note = (string?)null, active = true })));
        Assert.Equal("9007199254740993", result.GetProperty("identity").GetString());
        Assert.Equal("id", result.GetProperty("identityColumn").GetString());
        Assert.Equal("primary", result.GetProperty("databaseName").GetString());
        Assert.False(result.TryGetProperty("affectedRows", out _));
        using var observer = fixture.Sessions.AcquireClient();
        var stored = observer.Client.Ado.GetDataTable("SELECT name,amount,note,active,status FROM items WHERE id=9007199254740993").Rows[0];
        Assert.Equal("O'Brien", stored["name"]);
        Assert.Equal(10.5, Convert.ToDouble(stored["amount"]));
        Assert.Equal(DBNull.Value, stored["note"]);
        Assert.Equal(1L, Convert.ToInt64(stored["active"]));
        Assert.Equal("new", stored["status"]);
        Assert.Equal(9007199254740993L, observer.Client.Ado.GetLong("SELECT item_id FROM audit"));
    }

    [Fact]
    public async Task InvalidRows_DoNotWrite()
    {
        using var fixture = DatabaseRoleTests.Create();
        using (var db = fixture.Sessions.AcquireClient())
            db.Client.Ado.ExecuteCommand("CREATE TABLE items(id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL UNIQUE); CREATE TABLE plain(id INTEGER PRIMARY KEY, name TEXT)");
        var tool = fixture.Host.Services.GetRequiredService<CommandTools>();
        foreach (var raw in new[] { "null", "[]", "{}", "{\"id\":1}", "{\"missing\":1}", "{\"name\":[]}", "{\"name\":{}}", "{\"name\":1,\"name\":2}", "{\"items.name\":1}" })
            DatabaseRoleTests.Error(await tool.InsertRow("items", JsonSerializer.Deserialize<JsonElement>(raw)), 1004);
        DatabaseRoleTests.Error(await tool.InsertRow("items", JsonSerializer.SerializeToElement(new { name = new string('x', 4 * 1024 * 1024) })), 1004);
        DatabaseRoleTests.Error(await tool.InsertRow("items", JsonSerializer.SerializeToElement(Enumerable.Range(0, 129).ToDictionary(i => "c" + i, i => i))), 1004);
        DatabaseRoleTests.Error(await tool.InsertRow("plain", JsonSerializer.SerializeToElement(new { name = "no identity" })), 1004);
        DatabaseRoleTests.Error(await tool.InsertRow("main.items", JsonSerializer.SerializeToElement(new { name = "qualified" })), 1004);
        using (var missing = JsonDocument.Parse(await tool.InsertRow("missing", JsonSerializer.SerializeToElement(new { name = "missing" }))))
            Assert.False(missing.RootElement.GetProperty("success").GetBoolean());
        using (var db = fixture.Sessions.AcquireClient()) Assert.Equal(0, db.Client.Ado.GetInt("SELECT count(*) FROM items"));
        DatabaseRoleTests.Success(await tool.InsertRow("items", JsonSerializer.SerializeToElement(new { name = "unique" })));
        using var duplicate = JsonDocument.Parse(await tool.InsertRow("items", JsonSerializer.SerializeToElement(new { name = "unique" })));
        Assert.False(duplicate.RootElement.GetProperty("success").GetBoolean());
        using var observer = fixture.Sessions.AcquireClient();
        Assert.Equal(1, observer.Client.Ado.GetInt("SELECT count(*) FROM items"));
        Assert.Equal(0, observer.Client.Ado.GetInt("SELECT count(*) FROM plain"));
    }

    [Fact]
    public async Task WriteAccess_AllowsInsertWithoutSchemaPermission()
    {
        using var fixture = DatabaseRoleTests.Create(new() { Access = DatabaseToolAccess.Write });
        using (var db = fixture.Sessions.AcquireClient())
            db.Client.Ado.ExecuteCommand("CREATE TABLE items(id INTEGER PRIMARY KEY AUTOINCREMENT,name TEXT)");
        var result = DatabaseRoleTests.Success(await fixture.Host.Services.GetRequiredService<CommandTools>()
            .InsertRow("items", JsonSerializer.SerializeToElement(new { name = "writer" })));
        Assert.Equal("1", result.GetProperty("identity").GetString());
        using var observer = fixture.Sessions.AcquireClient();
        Assert.Equal("writer", observer.Client.Ado.GetString("SELECT name FROM items"));
    }

    [Fact]
    public async Task ReadOnlyAndCancellation_DoNotWrite()
    {
        using var fixture = DatabaseRoleTests.Create(new DatabaseExecutionOptions(MaxConcurrent: 1));
        using (var db = fixture.Sessions.AcquireClient())
            db.Client.Ado.ExecuteCommand("CREATE TABLE items(id INTEGER PRIMARY KEY AUTOINCREMENT,name TEXT)");
        var tool = fixture.Host.Services.GetRequiredService<CommandTools>();
        var row = JsonSerializer.SerializeToElement(new { name = "cancelled" });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tool.InsertRow("items", row, cancellationToken: new CancellationToken(true)));
        using (var held = fixture.Sessions.AcquireClient())
            DatabaseRoleTests.Error(await tool.InsertRow("items", row, commandTimeoutSeconds: 1), 1011);
        using (var db = fixture.Sessions.AcquireClient()) Assert.Equal(0, db.Client.Ado.GetInt("SELECT count(*) FROM items"));
        DatabaseRoleTests.Success(await tool.InsertRow("items", row));

        using var readOnly = DatabaseRoleTests.Create(access: "read-only");
        DatabaseRoleTests.Error(await readOnly.Host.Services.GetRequiredService<CommandTools>().InsertRow("markers", row), 1010);
        using var policy = new ToolExposurePolicy();
        Assert.DoesNotContain("insert_row", policy.Select(new ToolCatalog(), readOnly.Service, new()));
        using var observer = readOnly.Sessions.AcquireClient();
        Assert.Equal(1, observer.Client.Ado.GetInt("SELECT count(*) FROM markers"));
    }
}
