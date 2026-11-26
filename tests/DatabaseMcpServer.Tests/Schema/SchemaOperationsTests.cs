using System.Text.Json;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseMcpServer.Tests;

public class SchemaOperationsTests
{
    [Fact]
    public async Task SeparateCommands_InvalidateMetadataReadCacheOnNamedConnection()
    {
        using var fixture = DatabaseRoleTests.Create();
        var reads = fixture.Host.Services.GetRequiredService<SchemaReadOperations>();
        var commands = fixture.Host.Services.GetRequiredService<SchemaCommandOperations>();
        var created = JsonSerializer.SerializeToElement(commands.CreateTable("split_schema", [new() { Name = "id", DataType = "integer" }], databaseName: "analytics"));
        Assert.True(created.GetProperty("success").GetBoolean());
        var before = JsonSerializer.SerializeToElement(await reads.GetTableSchema("split_schema", databaseName: "analytics"), JsonSerializationDefaults.CompactCamelCase);
        Assert.Single(before.GetProperty("data").GetProperty("columns").EnumerateArray());
        var changed = JsonSerializer.SerializeToElement(commands.AddColumn("split_schema", new() { Name = "label", DataType = "text", Nullable = true }, "analytics"));
        Assert.True(changed.GetProperty("success").GetBoolean());
        var after = JsonSerializer.SerializeToElement(await reads.GetTableSchema("split_schema", databaseName: "analytics"), JsonSerializationDefaults.CompactCamelCase);
        Assert.Equal(new[] { "id", "label" }, after.GetProperty("data").GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("name").GetString()));
        var primary = JsonSerializer.SerializeToElement(reads.IsAnyTable("split_schema", "primary"));
        Assert.False(primary.GetProperty("exists").GetBoolean());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SqliteTruncateTable_HandlesEmptyAndNonAutoincrementTables(bool hasAutoincrementTable)
    {
        using var fixture = DatabaseRoleTests.Create();
        var commands = fixture.Host.Services.GetRequiredService<SchemaCommandOperations>();
        using (var lease = fixture.Sessions.AcquireClient("analytics"))
        {
            lease.Client.Ado.ExecuteCommand($"CREATE TABLE trunc_empty(id INTEGER PRIMARY KEY {(hasAutoincrementTable ? "AUTOINCREMENT" : "")}, label TEXT)");
            lease.Client.Ado.ExecuteCommand("CREATE TABLE trunc_noinc(id TEXT PRIMARY KEY)");
            lease.Client.Ado.ExecuteCommand("INSERT INTO trunc_noinc(id) VALUES ('row1')");
        }

        Assert.True(JsonSerializer.SerializeToElement(commands.TruncateTable("trunc_empty", "analytics")).GetProperty("success").GetBoolean());
        Assert.True(JsonSerializer.SerializeToElement(commands.TruncateTable("trunc_noinc", "analytics")).GetProperty("success").GetBoolean());
        using (var observer = fixture.Sessions.AcquireClient("analytics"))
            Assert.Equal(0, observer.Client.Ado.GetInt("SELECT COUNT(*) FROM trunc_noinc"));

        var missing = JsonSerializer.SerializeToElement(commands.TruncateTable("trunc_missing", "analytics"), JsonSerializationDefaults.CompactCamelCase);
        Assert.False(missing.GetProperty("success").GetBoolean());
        Assert.Equal((int)DatabaseMcpServer.Models.DatabaseErrorCode.QueryExecutionFailed, missing.GetProperty("errorCode").GetInt32());
    }

    [Theory]
    [InlineData("trunc_reset")]
    [InlineData("main.trunc_reset")]
    [InlineData("MAIN.TRUNC_RESET")]
    public void SqliteTruncateTable_ResetsOnlyTargetSequence(string tableName)
    {
        using var fixture = DatabaseRoleTests.Create();
        var commands = fixture.Host.Services.GetRequiredService<SchemaCommandOperations>();
        foreach (var database in new[] { "primary", "analytics" })
        {
            using var lease = fixture.Sessions.AcquireClient(database);
            lease.Client.Ado.ExecuteCommand("CREATE TABLE trunc_reset(id INTEGER PRIMARY KEY AUTOINCREMENT); INSERT INTO trunc_reset(id) VALUES (41)");
            lease.Client.Ado.ExecuteCommand("CREATE TABLE trunc_keep(id INTEGER PRIMARY KEY AUTOINCREMENT); INSERT INTO trunc_keep(id) VALUES (99)");
        }

        Assert.True(JsonSerializer.SerializeToElement(commands.TruncateTable(tableName, "analytics")).GetProperty("success").GetBoolean());

        using (var observer = fixture.Sessions.AcquireClient("analytics"))
        {
            Assert.Equal(0, observer.Client.Ado.GetInt("SELECT COUNT(*) FROM trunc_reset"));
            Assert.Equal(1, observer.Client.Ado.GetInt("INSERT INTO trunc_reset DEFAULT VALUES RETURNING id"));
            Assert.Equal(100, observer.Client.Ado.GetInt("INSERT INTO trunc_keep DEFAULT VALUES RETURNING id"));
        }
        using var primary = fixture.Sessions.AcquireClient("primary");
        Assert.Equal(41, primary.Client.Ado.GetInt("SELECT id FROM trunc_reset"));
        Assert.Equal(42, primary.Client.Ado.GetInt("INSERT INTO trunc_reset DEFAULT VALUES RETURNING id"));
    }

    [Fact]
    public async Task CompositeIndex_AndDropIndex_ExecuteEndToEndOnSqlite()
    {
        using var fixture = DatabaseRoleTests.Create();
        var commands = fixture.Host.Services.GetRequiredService<SchemaCommandOperations>();
        var reads = fixture.Host.Services.GetRequiredService<SchemaReadOperations>();

        Assert.True(JsonSerializer.SerializeToElement(commands.CreateTable("bulk_orders",
        [
            new() { Name = "id", DataType = "integer", PrimaryKey = true },
            new() { Name = "code", DataType = "text", Nullable = false },
            new() { Name = "amount", DataType = "integer", Nullable = false }
        ], databaseName: "analytics")).GetProperty("success").GetBoolean());

        Assert.True(JsonSerializer.SerializeToElement(commands.CreateIndex(
            "bulk_orders", "ix_orders_code_amount", databaseName: "analytics", columnNames: ["code", "amount"]))
            .GetProperty("success").GetBoolean());

        var schemaBeforeDrop = JsonSerializer.SerializeToElement(
            await reads.GetTableSchema("bulk_orders", "analytics", refresh: true, sections: ["indexes"]),
            JsonSerializationDefaults.CompactCamelCase);
        Assert.Contains("ix_orders_code_amount", schemaBeforeDrop.ToString());

        Assert.True(JsonSerializer.SerializeToElement(commands.DropIndex("bulk_orders", "ix_orders_code_amount", "analytics"))
            .GetProperty("success").GetBoolean());

        var schemaAfterDrop = JsonSerializer.SerializeToElement(
            await reads.GetTableSchema("bulk_orders", "analytics", refresh: true, sections: ["indexes"]),
            JsonSerializationDefaults.CompactCamelCase);
        Assert.DoesNotContain("ix_orders_code_amount", schemaAfterDrop.ToString());
    }
}
