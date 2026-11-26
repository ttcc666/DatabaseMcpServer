using DatabaseMcpServer.Tools.Management;

namespace DatabaseMcpServer.Tests.Integration;

[Trait("Category", "Integration")]
public class ProviderObjectCatalogIntegrationTests
{
    [ProviderTheory]
    [InlineData("MySql")]
    public async Task UnifiedListing_PreservesProviderObjectsAndPagesWithoutAliases(string provider)
    {
        using var fixture = new ProviderFixture(provider);
        var prefix = "catalog_" + Guid.NewGuid().ToString("N");
        var db = fixture.Observer.Ado;
        db.ExecuteCommand($"CREATE TABLE {prefix}_a(id int)");
        db.ExecuteCommand($"CREATE TABLE {prefix}_b(id int)");
        db.ExecuteCommand($"CREATE VIEW {prefix}_view AS SELECT id FROM {prefix}_a");
        db.ExecuteCommand($"CREATE PROCEDURE {prefix}_proc() SELECT 1");
        db.ExecuteCommand($"CREATE FUNCTION {prefix}_func() RETURNS INT DETERMINISTIC RETURN 1");
        db.ExecuteCommand($"CREATE TRIGGER {prefix}_trigger BEFORE INSERT ON {prefix}_a FOR EACH ROW SET NEW.id=1");
        try
        {
            var tools = fixture.Tool<ObjectMetadataTools>();
            var first = DatabaseRoleTests.Success(await tools.ListObjects("table", namePrefix: prefix, limit: 1).Json());
            Assert.Equal("keyset", first.GetProperty("pagingMode").GetString());
            Assert.Equal(prefix + "_a", first.GetProperty("objects")[0].GetProperty("name").GetString());
            var cursor = first.GetProperty("nextCursor").GetString();
            Assert.NotNull(cursor);
            var second = DatabaseRoleTests.Success(await tools.ListObjects("table", namePrefix: prefix, limit: 1, cursor: cursor).Json());
            Assert.Equal(prefix + "_b", second.GetProperty("objects")[0].GetProperty("name").GetString());
            Assert.Equal(System.Text.Json.JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);
            foreach (var (kind, suffix) in new[] { ("view", "view"), ("procedure", "proc"), ("function", "func"), ("trigger", "trigger") })
            {
                var result = DatabaseRoleTests.Success(await tools.ListObjects(kind, namePrefix: prefix,
                    tableName: kind == "trigger" ? prefix + "_a" : null).Json());
                var item = Assert.Single(result.GetProperty("objects").EnumerateArray());
                Assert.Equal(prefix + "_" + suffix, item.GetProperty("name").GetString());
                Assert.Equal(kind, item.GetProperty("kind").GetString());
            }
            Assert.Empty(DatabaseRoleTests.Success(await tools.ListObjects("table", schema: "unsupported").Json()).GetProperty("objects").EnumerateArray());
            Assert.Contains(DatabaseRoleTests.Success(await tools.ListObjects("trigger").Json()).GetProperty("objects").EnumerateArray(),
                item => item.GetProperty("name").GetString() == prefix + "_trigger");
            DatabaseRoleTests.Error(await tools.ListObjects("table", namePrefix: "different", limit: 1, cursor: cursor).Json(), 1004);
        }
        finally
        {
            db.ExecuteCommand($"DROP TRIGGER {prefix}_trigger");
            db.ExecuteCommand($"DROP FUNCTION {prefix}_func");
            db.ExecuteCommand($"DROP PROCEDURE {prefix}_proc");
            db.ExecuteCommand($"DROP VIEW {prefix}_view");
            db.ExecuteCommand($"DROP TABLE {prefix}_a");
            db.ExecuteCommand($"DROP TABLE {prefix}_b");
        }
    }
}
