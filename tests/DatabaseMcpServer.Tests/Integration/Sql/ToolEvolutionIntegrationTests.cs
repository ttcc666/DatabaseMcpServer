using System.Text.Json;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Tools.Management;
using DatabaseMcpServer.Tools.Query;

namespace DatabaseMcpServer.Tests.Integration;

[Trait("Category", "Integration")]
public class ToolEvolutionIntegrationTests
{
    [ProviderTheory]
    [InlineData("SqlServer")]
    [InlineData("PostgreSQL")]
    public async Task MetadataExplainAndAtomicBatch_WorkAgainstRealProvider(string provider)
    {
        using var fixture = new ProviderFixture(provider);
        var suffix = Guid.NewGuid().ToString("N");
        var parent = "p_" + suffix;
        var child = "c_" + suffix;
        var view = "v_" + suffix;
        fixture.Observer.Ado.ExecuteCommand($"CREATE TABLE {parent}(a int,b int,PRIMARY KEY(b,a))");
        fixture.Observer.Ado.ExecuteCommand($"CREATE TABLE {child}(x int,y int,PRIMARY KEY(y,x),CONSTRAINT fk_{suffix} FOREIGN KEY(y,x) REFERENCES {parent}(b,a))");
        fixture.Observer.Ado.ExecuteCommand($"CREATE VIEW {view} AS SELECT x,y FROM {child}");
        try
        {
            var metadata = fixture.Tool<ObjectMetadataTools>();
            var schema = provider == "SqlServer" ? "dbo" : "public";
            var listed = DatabaseRoleTests.Success(await metadata.ListObjects("table", schema: schema, namePrefix: child).Json());
            Assert.Equal(child, listed.GetProperty("objects")[0].GetProperty("name").GetString());
            var definition = DatabaseRoleTests.Success(await metadata.GetObjectDefinition("view", view, schema).Json());
            Assert.Equal("available", definition.GetProperty("availability").GetString());
            Assert.Contains(child, definition.GetProperty("definition").GetString());
            var data = DatabaseRoleTests.Success(await fixture.Tool<SchemaTools>().GetTableSchema(child, sections: ["foreignKeys", "constraints", "comments"])).GetProperty("data");
            Assert.Equal("available", data.GetProperty("foreignKeys").GetProperty("status").GetString());
            Assert.Equal(new[] { "y", "x" }, data.GetProperty("foreignKeys").GetProperty("data")[0].GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("sourceColumn").GetString()));
            Assert.Equal("available", data.GetProperty("constraints").GetProperty("status").GetString());
            Assert.Equal("available", data.GetProperty("comments").GetProperty("status").GetString());
            // A row-dependent divide by zero fails at execution but can be planned without evaluation.
            fixture.Observer.Ado.ExecuteCommand($"INSERT INTO {parent} VALUES(0,1)");
            var plan = DatabaseRoleTests.Success(await fixture.Tool<ExplainTools>().ExplainQuery($"SELECT 10/a FROM {parent} WHERE b=@b",
                parameters: [new() { Name = "b", Value = JsonSerializer.SerializeToElement(1) }]).Json());
            Assert.False(plan.GetProperty("executed").GetBoolean());
            Assert.NotEmpty(plan.GetProperty("plan").EnumerateArray());
            Assert.Equal(provider == "SqlServer" ? "types-only" : "bound-values", plan.GetProperty("parameterMode").GetString());
            var nullPlan = DatabaseRoleTests.Success(await fixture.Tool<ExplainTools>().ExplainQuery($"SELECT a FROM {parent} WHERE b=@nullable",
                parameters: [new() { Name = "nullable", DbType = DatabaseParameterType.Int32, Value = JsonSerializer.SerializeToElement((int?)null) }]).Json());
            Assert.NotEmpty(nullPlan.GetProperty("plan").EnumerateArray());
            Assert.True((await fixture.Tool<ExplainTools>().ExplainQuery($"SELECT missing_column FROM {parent}")).IsError);
            Assert.True((await fixture.StructuredQuery.QueryDatabase($"SELECT 10/a FROM {parent}")).IsError);
            await fixture.AssertUsable();
            var committed = DatabaseRoleTests.Success(await fixture.Command.BatchExecuteCommands(
                [new() { Sql = $"INSERT INTO {parent} VALUES(2,2)" }, new() { Sql = $"INSERT INTO {child} VALUES(2,2)" }], atomic: true));
            Assert.Equal("committed", committed.GetProperty("transactionOutcome").GetString());
            using var failed = JsonDocument.Parse(await fixture.Command.BatchExecuteCommands(
                [new() { Sql = $"INSERT INTO {parent} VALUES(3,3)" }, new() { Sql = $"INSERT INTO {parent} VALUES(2,2)" }, new() { Sql = $"INSERT INTO {parent} VALUES(4,4)" }], atomic: true));
            Assert.Equal("rolled-back", failed.RootElement.GetProperty("transactionOutcome").GetString());
            Assert.Equal(0, fixture.Observer.Ado.GetInt($"SELECT COUNT(*) FROM {parent} WHERE a IN(3,4)"));
            Assert.Equal(1, fixture.Observer.Ado.GetInt($"SELECT COUNT(*) FROM {child}"));
            await fixture.AssertUsable();
        }
        finally
        {
            fixture.Observer.Ado.ExecuteCommand($"DROP VIEW {view}");
            fixture.Observer.Ado.ExecuteCommand($"DROP TABLE {child}");
            fixture.Observer.Ado.ExecuteCommand($"DROP TABLE {parent}");
        }
    }
}
