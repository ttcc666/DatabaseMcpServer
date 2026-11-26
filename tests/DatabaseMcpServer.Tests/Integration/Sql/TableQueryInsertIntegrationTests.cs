using System.Text.Json;
using DatabaseMcpServer.Models;

namespace DatabaseMcpServer.Tests.Integration;

[Trait("Category", "Integration")]
public class TableQueryInsertIntegrationTests
{
    [ProviderTheory]
    [InlineData("MySql")]
    [InlineData("PostgreSQL")]
    [InlineData("SqlServer")]
    public async Task QueryOptions_UseProviderOrderingAndTypedFilters(string provider)
    {
        using var fixture = new ProviderFixture(provider);
        const string table = "table_query_options";
        fixture.Observer.Ado.ExecuteCommand($"CREATE TABLE {table}(id BIGINT PRIMARY KEY, label VARCHAR(80), score INT NULL)");
        try
        {
            fixture.Observer.Ado.ExecuteCommand($"INSERT INTO {table} VALUES(1,'null',7),(2,'O''Brien',7),(3,NULL,NULL),(4,'null',NULL)");
            foreach (var direction in new[] { TableSortDirection.Asc, TableSortDirection.Desc })
            {
                var expected = fixture.Observer.Ado.SqlQuery<long>($"SELECT id FROM {table} ORDER BY score {direction},id ASC");
                var actual = new List<long>();
                for (var page = 1; page <= 3; page++)
                {
                    using var result = ProviderFixture.Success(await fixture.Query.QueryTablePage(table, page: page, pageSize: 2, columns: ["id"],
                        orderBy: [new() { Column = "score", Direction = direction }]));
                    Assert.Equal("offset", result.RootElement.GetProperty("pagingMode").GetString());
                    Assert.Equal(JsonValueKind.Null, result.RootElement.GetProperty("nextCursor").ValueKind);
                    Assert.Equal(page < 3 ? 2 : 0, result.RootElement.GetProperty("rowCount").GetInt32());
                    Assert.Equal(page < 3, result.RootElement.GetProperty("mayHaveMore").GetBoolean());
                    foreach (var row in result.RootElement.GetProperty("data").EnumerateArray())
                    {
                        Assert.Equal("id", Assert.Single(row.EnumerateObject()).Name);
                        actual.Add(row.GetProperty("id").GetInt64());
                    }
                }
                Assert.Equal(expected, actual);
            }
            using var filtered = ProviderFixture.Success(await fixture.Query.QueryTablePage(table, columns: ["id"], filters:
            [
                new() { Column = "label", Op = TableFilterOperator.Eq, Value = JsonSerializer.SerializeToElement("null") },
                new() { Column = "id", Op = TableFilterOperator.Gt, Value = JsonSerializer.SerializeToElement("1"), DbType = DatabaseParameterType.Int64 }
            ]));
            Assert.Equal(4, Assert.Single(filtered.RootElement.GetProperty("data").EnumerateArray()).GetProperty("id").GetInt32());
            using var nulls = ProviderFixture.Success(await fixture.Query.QueryTablePage(table, filters: [new() { Column = "label", Op = TableFilterOperator.IsNull }]));
            Assert.Equal(3, Assert.Single(nulls.RootElement.GetProperty("data").EnumerateArray()).GetProperty("id").GetInt32());
            await fixture.AssertUsable();
        }
        finally { fixture.Observer.Ado.ExecuteCommand($"DROP TABLE {table}"); }
    }

    [ProviderTheory]
    [InlineData("MySql")]
    [InlineData("SqlServer")]
    public async Task Insert_ReturnsBigIdentityWithDefaultsAndTrigger(string provider)
    {
        using var fixture = new ProviderFixture(provider);
        var identity = provider == "MySql" ? "BIGINT PRIMARY KEY AUTO_INCREMENT" : "BIGINT IDENTITY(3000000001,1) PRIMARY KEY";
        fixture.Observer.Ado.ExecuteCommand($"CREATE TABLE inserted_items(id {identity}, name VARCHAR(80) NOT NULL UNIQUE, note VARCHAR(80), amount DECIMAL(12,2), status VARCHAR(20) DEFAULT 'new')");
        fixture.Observer.Ado.ExecuteCommand("CREATE TABLE inserted_audit(item_id BIGINT)");
        try
        {
            if (provider == "MySql") fixture.Observer.Ado.ExecuteCommand("ALTER TABLE inserted_items AUTO_INCREMENT=3000000001");
            fixture.Observer.Ado.ExecuteCommand(provider == "MySql"
                ? "CREATE TRIGGER inserted_item AFTER INSERT ON inserted_items FOR EACH ROW INSERT INTO inserted_audit VALUES(NEW.id)"
                : "CREATE TRIGGER inserted_item ON inserted_items AFTER INSERT AS INSERT INTO inserted_audit SELECT id FROM inserted");
            using var result = ProviderFixture.Success(await fixture.Command.InsertRow("inserted_items",
                JsonSerializer.SerializeToElement(new { name = "O'Brien", note = (string?)null, amount = 10.25 })));
            Assert.Equal("3000000001", result.RootElement.GetProperty("identity").GetString());
            Assert.Equal("id", result.RootElement.GetProperty("identityColumn").GetString());
            var row = fixture.Observer.Ado.GetDataTable("SELECT * FROM inserted_items WHERE id=3000000001").Rows[0];
            Assert.Equal("O'Brien", row["name"]);
            Assert.Equal(DBNull.Value, row["note"]);
            Assert.Equal(10.25m, Convert.ToDecimal(row["amount"]));
            Assert.Equal("new", row["status"]);
            Assert.Equal(3000000001L, fixture.Observer.Ado.GetLong("SELECT item_id FROM inserted_audit"));
            using var duplicate = JsonDocument.Parse(await fixture.Command.InsertRow("inserted_items", JsonSerializer.SerializeToElement(new { name = "O'Brien" })));
            Assert.False(duplicate.RootElement.GetProperty("success").GetBoolean());
            DatabaseRoleTests.Error(await fixture.Command.InsertRow("inserted_items", JsonSerializer.SerializeToElement(new { id = 5, name = "explicit" })), 1004);
            Assert.Equal(1, fixture.Observer.Ado.GetInt("SELECT count(*) FROM inserted_items"));
            Assert.Equal(1, fixture.Observer.Ado.GetInt("SELECT count(*) FROM inserted_audit"));
            await fixture.AssertUsable();
        }
        finally
        {
            fixture.Observer.Ado.ExecuteCommand("DROP TABLE inserted_items");
            fixture.Observer.Ado.ExecuteCommand("DROP TABLE inserted_audit");
        }
    }

    [ProviderTheory]
    [InlineData("PostgreSQL")]
    public async Task PostgreSqlInsert_IsUnsupportedBeforeWrite(string provider)
    {
        using var fixture = new ProviderFixture(provider);
        fixture.Observer.Ado.ExecuteCommand("CREATE TABLE unsupported_insert(id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY, name TEXT)");
        try
        {
            DatabaseRoleTests.Error(await fixture.Command.InsertRow("unsupported_insert", JsonSerializer.SerializeToElement(new { name = "blocked" })), 1012);
            Assert.Equal(0, fixture.Observer.Ado.GetInt("SELECT count(*) FROM unsupported_insert"));
        }
        finally { fixture.Observer.Ado.ExecuteCommand("DROP TABLE unsupported_insert"); }
    }

    [ProviderTheory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task SqlServerInsert_ReturnsNonPositiveIdentity(long seed)
    {
        using var fixture = new ProviderFixture("SqlServer");
        fixture.Observer.Ado.ExecuteCommand($"CREATE TABLE signed_identity(id BIGINT IDENTITY({seed},1) PRIMARY KEY, name VARCHAR(20))");
        try
        {
            using var result = ProviderFixture.Success(await fixture.Command.InsertRow("signed_identity", JsonSerializer.SerializeToElement(new { name = "signed" })));
            Assert.Equal(seed.ToString(System.Globalization.CultureInfo.InvariantCulture), result.RootElement.GetProperty("identity").GetString());
            Assert.Equal(seed, fixture.Observer.Ado.GetLong("SELECT id FROM signed_identity"));
            Assert.Equal(1, fixture.Observer.Ado.GetInt("SELECT count(*) FROM signed_identity"));
        }
        finally { fixture.Observer.Ado.ExecuteCommand("DROP TABLE signed_identity"); }
    }

    [ProviderTheory]
    [InlineData("SqlServer")]
    public async Task SqlServerInsert_RejectsAmbiguousSchemasBeforeWriting(string provider)
    {
        using var fixture = new ProviderFixture(provider);
        fixture.Observer.Ado.ExecuteCommand("CREATE SCHEMA duplicate_schema");
        fixture.Observer.Ado.ExecuteCommand("CREATE TABLE dbo.ambiguous_items(id BIGINT IDENTITY PRIMARY KEY, name VARCHAR(20)); CREATE TABLE duplicate_schema.ambiguous_items(id BIGINT, name VARCHAR(20))");
        try
        {
            var row = JsonSerializer.SerializeToElement(new { name = "blocked" });
            DatabaseRoleTests.Error(await fixture.Command.InsertRow("ambiguous_items", row), 1004);
            DatabaseRoleTests.Error(await fixture.Command.InsertRow("dbo.ambiguous_items", row), 1004);
            Assert.Equal(0, fixture.Observer.Ado.GetInt("SELECT count(*) FROM dbo.ambiguous_items"));
            Assert.Equal(0, fixture.Observer.Ado.GetInt("SELECT count(*) FROM duplicate_schema.ambiguous_items"));
        }
        finally
        {
            fixture.Observer.Ado.ExecuteCommand("DROP TABLE dbo.ambiguous_items; DROP TABLE duplicate_schema.ambiguous_items; DROP SCHEMA duplicate_schema");
        }
    }
}
