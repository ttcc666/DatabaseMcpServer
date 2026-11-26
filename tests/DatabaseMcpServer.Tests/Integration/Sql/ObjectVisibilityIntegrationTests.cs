using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Tools.Management;
using DatabaseMcpServer.Tools.Query;

namespace DatabaseMcpServer.Tests.Integration;

[Trait("Category", "Integration")]
public class ObjectVisibilityIntegrationTests
{
    [ProviderTheory]
    [InlineData("SqlServer")]
    [InlineData("PostgreSQL")]
    public async Task Metadata_SeparatesSchemasAndRespectsDatabasePermissions(string provider)
    {
        using var fixture = new ProviderFixture(provider);
        var suffix = Guid.NewGuid().ToString("N");
        var first = "s1_" + suffix;
        var second = "s2_" + suffix;
        var role = "r_" + suffix;
        var password = "It!" + Guid.NewGuid().ToString("N");
        const string table = "odd's table";
        const string view = "select's view";
        string Quote(string value) => provider == "SqlServer" ? $"[{value}]" : $"\"{value}\"";
        var tableOne = $"{first}.{Quote(table)}";
        var tableTwo = $"{second}.{Quote(table)}";
        var viewOne = $"{first}.{Quote(view)}";
        var viewTwo = $"{second}.{Quote(view)}";
        var db = fixture.Observer.Ado;
        db.ExecuteCommand($"CREATE SCHEMA {first}");
        db.ExecuteCommand($"CREATE SCHEMA {second}");
        db.ExecuteCommand($"CREATE TABLE {tableOne}(a int PRIMARY KEY)");
        db.ExecuteCommand($"CREATE TABLE {tableTwo}(b int PRIMARY KEY)");
        db.ExecuteCommand($"CREATE VIEW {viewOne} AS SELECT a FROM {tableOne}");
        db.ExecuteCommand($"CREATE VIEW {viewTwo} AS SELECT b FROM {tableTwo}");
        string restrictedConnection;
        if (provider == "SqlServer")
        {
            db.ExecuteCommand($"CREATE LOGIN {role} WITH PASSWORD='{password}', CHECK_POLICY=OFF");
            db.ExecuteCommand($"CREATE USER {role} FOR LOGIN {role}");
            db.ExecuteCommand($"GRANT SELECT ON {tableOne} TO {role}");
            db.ExecuteCommand($"GRANT SELECT ON {viewOne} TO {role}");
            db.ExecuteCommand($"DENY VIEW DEFINITION ON OBJECT::{viewOne} TO {role}");
            restrictedConnection = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(fixture.Observer.CurrentConnectionConfig.ConnectionString)
            { UserID = role, Password = password }.ConnectionString;
        }
        else
        {
            db.ExecuteCommand($"CREATE ROLE {role} LOGIN PASSWORD '{password}'");
            db.ExecuteCommand($"GRANT USAGE ON SCHEMA {first},{second} TO {role}");
            db.ExecuteCommand($"GRANT SELECT ON {tableOne},{viewOne} TO {role}");
            restrictedConnection = new Npgsql.NpgsqlConnectionStringBuilder(fixture.Observer.CurrentConnectionConfig.ConnectionString)
            { Username = role, Password = password }.ConnectionString;
        }
        try
        {
            var metadata = fixture.Tool<ObjectMetadataTools>();
            foreach (var (schema, column) in new[] { (first, "a"), (second, "b") })
            {
                var listed = DatabaseRoleTests.Success(await metadata.ListObjects("table", schema: schema, namePrefix: table).Json());
                var item = Assert.Single(listed.GetProperty("objects").EnumerateArray());
                Assert.Equal(schema, item.GetProperty("schema").GetString());
                Assert.Equal(table, item.GetProperty("name").GetString());
                var structure = DatabaseRoleTests.Success(await fixture.Tool<SchemaTools>().GetTableSchema(schema + "." + table, sections: ["constraints"]));
                var columns = structure.GetProperty("data").GetProperty("constraints").GetProperty("data")[0].GetProperty("columns");
                Assert.Equal(column, Assert.Single(columns.EnumerateArray()).GetProperty("columnName").GetString());
            }
            using (var restricted = new ProviderFixture(provider))
            {
                Assert.True(restricted.Tool<IDatabaseConfigService>().UpdateDatabase("integration", new DatabaseConnectionUpdate
                { ApplyConnectionString = true, ConnectionString = restrictedConnection }).Success);
                var tools = restricted.Tool<ObjectMetadataTools>();
                var visible = DatabaseRoleTests.Success(await tools.ListObjects("table", schema: first).Json());
                Assert.Single(visible.GetProperty("objects").EnumerateArray());
                var hidden = DatabaseRoleTests.Success(await tools.ListObjects("table", schema: second).Json());
                Assert.Empty(hidden.GetProperty("objects").EnumerateArray());
                var hiddenSchema = DatabaseRoleTests.Success(await restricted.Tool<SchemaTools>().GetTableSchema(second + "." + table, sections: ["constraints"]));
                Assert.Equal("unavailable", hiddenSchema.GetProperty("data").GetProperty("constraints").GetProperty("status").GetString());
                var hiddenDefinition = DatabaseRoleTests.Success(await tools.GetObjectDefinition("view", view, second).Json());
                Assert.Equal("definition-unavailable", hiddenDefinition.GetProperty("availability").GetString());
                var definition = DatabaseRoleTests.Success(await tools.GetObjectDefinition("view", view, first).Json());
                Assert.Equal(provider == "SqlServer" ? "definition-unavailable" : "available", definition.GetProperty("availability").GetString());
                Assert.True((await restricted.Tool<ExplainTools>().ExplainQuery($"SELECT * FROM {tableTwo}")).IsError);
                var allowedTablePlan = await restricted.Tool<ExplainTools>().ExplainQuery($"SELECT * FROM {tableOne}");
                Assert.Equal(provider == "SqlServer", allowedTablePlan.IsError == true); // SQL Server additionally requires SHOWPLAN.
                await restricted.AssertUsable();
            }
        }
        finally
        {
            if (provider == "SqlServer")
            {
                using var connection = new Microsoft.Data.SqlClient.SqlConnection(restrictedConnection);
                Microsoft.Data.SqlClient.SqlConnection.ClearPool(connection);
                db.ExecuteCommand($"DROP USER {role}");
                db.ExecuteCommand($"DROP LOGIN {role}");
            }
            else
            {
                using var connection = new Npgsql.NpgsqlConnection(restrictedConnection);
                Npgsql.NpgsqlConnection.ClearPool(connection);
                db.ExecuteCommand($"DROP OWNED BY {role}");
                db.ExecuteCommand($"DROP ROLE {role}");
            }
            db.ExecuteCommand($"DROP VIEW {viewOne}");
            db.ExecuteCommand($"DROP VIEW {viewTwo}");
            db.ExecuteCommand($"DROP TABLE {tableOne}");
            db.ExecuteCommand($"DROP TABLE {tableTwo}");
            db.ExecuteCommand($"DROP SCHEMA {first}");
            db.ExecuteCommand($"DROP SCHEMA {second}");
        }
    }

    [ProviderTheory]
    [InlineData("SqlServer")]
    public async Task EncryptedDefinition_IsUnavailableWithoutGuessingTheCause(string provider)
    {
        using var fixture = new ProviderFixture(provider);
        var view = "encrypted_" + Guid.NewGuid().ToString("N");
        fixture.Observer.Ado.ExecuteCommand($"CREATE VIEW {view} WITH ENCRYPTION AS SELECT 1 AS value");
        try
        {
            var result = DatabaseRoleTests.Success(await fixture.Tool<ObjectMetadataTools>().GetObjectDefinition("view", view).Json());
            Assert.Equal("definition-unavailable", result.GetProperty("availability").GetString());
            Assert.Equal(System.Text.Json.JsonValueKind.Null, result.GetProperty("definition").ValueKind);
        }
        finally { fixture.Observer.Ado.ExecuteCommand($"DROP VIEW {view}"); }
    }

    [ProviderTheory]
    [InlineData("PostgreSQL")]
    public async Task RoutineAndTriggerIdentities_RoundTripOverloadsAndDottedNames(string provider)
    {
        using var fixture = new ProviderFixture(provider);
        var schema = "identity_" + Guid.NewGuid().ToString("N");
        var db = fixture.Observer.Ado;
        db.ExecuteCommand($"CREATE SCHEMA {schema}");
        try
        {
            db.ExecuteCommand($"CREATE FUNCTION {schema}.\"f.odd\"(integer) RETURNS integer LANGUAGE sql AS 'SELECT $1'");
            db.ExecuteCommand($"CREATE FUNCTION {schema}.\"f.odd\"(text) RETURNS text LANGUAGE sql AS 'SELECT $1'");
            db.ExecuteCommand($"CREATE FUNCTION {schema}.on_change() RETURNS trigger LANGUAGE plpgsql AS 'BEGIN RETURN NEW; END'");
            db.ExecuteCommand($"CREATE TABLE {schema}.a(v int)");
            db.ExecuteCommand($"CREATE TABLE {schema}.\"a.b\"(v int)");
            db.ExecuteCommand($"CREATE TRIGGER \"b.c\" BEFORE INSERT ON {schema}.a FOR EACH ROW EXECUTE FUNCTION {schema}.on_change()");
            db.ExecuteCommand($"CREATE TRIGGER c BEFORE INSERT ON {schema}.\"a.b\" FOR EACH ROW EXECUTE FUNCTION {schema}.on_change()");
            var tools = fixture.Tool<ObjectMetadataTools>();
            foreach (var kind in new[] { "function", "trigger" })
            {
                var listed = DatabaseRoleTests.Success(await tools.ListObjects(kind, schema: schema, namePrefix: kind == "function" ? "f" : null).Json());
                var identities = listed.GetProperty("objects").EnumerateArray().Select(o => o.GetProperty("identity").GetString()!).ToArray();
                Assert.Equal(2, identities.Length);
                Assert.Equal(2, identities.Distinct().Count());
                var definitions = new List<string>();
                foreach (var identity in identities)
                {
                    var result = DatabaseRoleTests.Success(await tools.GetObjectDefinition(kind, identity, schema).Json());
                    Assert.Equal("available", result.GetProperty("availability").GetString());
                    definitions.Add(result.GetProperty("definition").GetString()!);
                }
                Assert.Equal(2, definitions.Distinct().Count());
            }
        }
        finally { db.ExecuteCommand($"DROP SCHEMA {schema} CASCADE"); }
    }
}
