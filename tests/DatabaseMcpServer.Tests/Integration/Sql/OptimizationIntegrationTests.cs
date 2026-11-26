using System.Text.Json;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Tools.Management;
using DatabaseMcpServer.Tools.Query;

namespace DatabaseMcpServer.Tests.Integration;

[Trait("Category", "Integration")]
public class OptimizationIntegrationTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [ProviderTheory]
    [InlineData("MySql")]
    public async Task CompatibleProduct_UsesDriverForBasicSchema(string provider)
    {
        using var fixture = new ProviderFixture(provider);
        var table = "basic_schema_" + Guid.NewGuid().ToString("N");
        fixture.Observer.Ado.ExecuteCommand($"CREATE TABLE {table}(id int NOT NULL AUTO_INCREMENT PRIMARY KEY,label varchar(20),INDEX ix_label(label))");
        try
        {
            var config = fixture.Tool<DatabaseMcpServer.Interfaces.IDatabaseConfigService>();
            Assert.True(config.UpdateDatabase("integration", new() { ApplyDbType = true, DbType = "Tidb" }).Success);
            var tool = fixture.Tool<SchemaTools>();
            var data = DatabaseRoleTests.Success(await tool.GetTableSchema(table,
                sections: ["columns", "primaryKeys", "identityColumns", "indexes", "foreignKeys"])).GetProperty("data");
            Assert.Equal(new[] { "id", "label" }, data.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("name").GetString()));
            Assert.Equal("id", Assert.Single(data.GetProperty("primaryKeys").EnumerateArray()).GetString());
            Assert.Equal("id", Assert.Single(data.GetProperty("identityColumns").EnumerateArray()).GetString());
            Assert.Contains("ix_label", data.GetProperty("indexes").GetRawText());
            Assert.Equal("unsupported", data.GetProperty("foreignKeys").GetProperty("status").GetString());
            DatabaseRoleTests.Error(await tool.GetTableSchema(table, maxRows: 1), 1013);
        }
        finally { fixture.Observer.Ado.ExecuteCommand($"DROP TABLE {table}"); }
    }

    [ProviderTheory]
    [InlineData("SqlServer")]
    public async Task SqlServerSchemaDialect_ReplacesDefaultsAndColumnRemarks(string provider)
    {
        using var fixture = new ProviderFixture(provider);
        var name = "dialect_" + Guid.NewGuid().ToString("N")[..12];
        fixture.Observer.Ado.ExecuteCommand($"CREATE TABLE {name}(id int NOT NULL,value int NULL DEFAULT 1)");
        try
        {
            var tool = fixture.Tool<SchemaTools>();
            DatabaseRoleTests.Success(tool.AddDefaultValue(name, "value", "7"));
            fixture.Observer.Ado.ExecuteCommand($"INSERT INTO {name}(id) VALUES(1)");
            Assert.Equal(7, fixture.Observer.Ado.GetInt($"SELECT value FROM {name} WHERE id=1"));
            DatabaseRoleTests.Success(tool.AddColumnRemark(name, "value", "first"));
            DatabaseRoleTests.Success(tool.AddColumnRemark(name, "value", "second"));
            Assert.Equal("second", fixture.Observer.Ado.GetString($"SELECT CONVERT(nvarchar(100),value) FROM sys.extended_properties WHERE major_id=OBJECT_ID('{name}') AND minor_id=COLUMNPROPERTY(OBJECT_ID('{name}'),'value','ColumnId') AND name='MS_Description'"));
            DatabaseRoleTests.Success(tool.DeleteColumnRemark(name, "value"));
            Assert.Equal(0, fixture.Observer.Ado.GetInt($"SELECT COUNT(*) FROM sys.extended_properties WHERE major_id=OBJECT_ID('{name}') AND name='MS_Description'"));
        }
        finally { fixture.Observer.Ado.ExecuteCommand($"DROP TABLE {name}"); }
    }

    [ProviderTheory]
    [InlineData("MySql")]
    [InlineData("PostgreSQL")]
    [InlineData("SqlServer")]
    public async Task LargeFields_PreserveProviderBehavior(string provider)
    {
        using var fixture = new ProviderFixture(provider);
        var text = provider switch { "SqlServer" => "REPLICATE(CAST('x' AS varchar(max)),8388608)", _ => "repeat('x',8388608)" };
        using var bounded = ProviderFixture.Success(await fixture.StructuredQuery.QueryDatabase($"SELECT {text} AS value", maxResponseBytes: 4096));
        Assert.True(bounded.RootElement.GetProperty("truncated").GetBoolean());
        Assert.Empty(bounded.RootElement.GetProperty("data").EnumerateArray());
        var literal = provider switch { "PostgreSQL" => "decode('0080ff','hex')", "MySql" => "UNHEX('0080ff')", _ => "0x0080ff" };
        using var scalar = ProviderFixture.Success(await fixture.StructuredQuery.QueryDatabase($"SELECT {literal} AS value", maxResponseBytes: 4096));
        Assert.Equal(new byte[] { 0, 128, 255 }, scalar.RootElement.GetProperty("data")[0].GetProperty("value").GetBytesFromBase64());
        await fixture.AssertUsable();
    }

    [ProviderTheory]
    [InlineData("MySql")]
    [InlineData("PostgreSQL")]
    [InlineData("SqlServer")]
    public async Task NativeSchemaCursorAndPlan_WorkOnActualProvider(string provider)
    {
        using var fixture = new ProviderFixture(provider);
        var table = "opt_" + Guid.NewGuid().ToString("N");
        fixture.Observer.Ado.ExecuteCommand($"CREATE TABLE {table}(a int NOT NULL,b varchar(20) NOT NULL,PRIMARY KEY(b,a))");
        try
        {
            fixture.Observer.Ado.ExecuteCommand($"INSERT INTO {table} VALUES(1,'a'),(2,'a'),(1,'b')");
            var schema = DatabaseRoleTests.Success(await fixture.Tool<SchemaTools>().GetTableSchema(table,
                sections: ["columns", "primaryKeys", "indexes", "foreignKeys", "constraints", "comments"])).GetProperty("data");
            Assert.Equal(new[] { "b", "a" }, schema.GetProperty("primaryKeys").EnumerateArray().Select(v => v.GetString()));
            Assert.Equal("available", schema.GetProperty("constraints").GetProperty("status").GetString());
            var first = DatabaseRoleTests.Success(await fixture.Tool<QueryTools>().QueryTablePage(table, pageSize: 2));
            fixture.Observer.Ado.ExecuteCommand($"DELETE FROM {table} WHERE b='a'");
            var next = DatabaseRoleTests.Success(await fixture.Tool<QueryTools>().QueryTablePage(table, pageSize: 2, cursor: first.GetProperty("nextCursor").GetString()));
            Assert.Equal("b", Assert.Single(next.GetProperty("data").EnumerateArray()).GetProperty("b").GetString());
            var plan = DatabaseRoleTests.Success(await fixture.Tool<ExplainTools>().ExplainQuery($"SELECT * FROM {table} WHERE a=@id",
                parameters: [new() { Name = "id", Value = JsonSerializer.SerializeToElement(1) }]));
            Assert.NotEmpty(plan.GetProperty("plan").EnumerateArray());
            if (provider == "MySql")
            {
                var definition = DatabaseRoleTests.Success(await fixture.Tool<ObjectMetadataTools>().GetObjectDefinition("table", table));
                Assert.Contains("CREATE TABLE", definition.GetProperty("definition").GetString());
                var missing = DatabaseRoleTests.Success(await fixture.Tool<ObjectMetadataTools>().GetObjectDefinition("table", table + "_missing"));
                Assert.Equal("definition-unavailable", missing.GetProperty("availability").GetString());
            }
            await fixture.AssertUsable();
        }
        finally { fixture.Observer.Ado.ExecuteCommand($"DROP TABLE {table}"); }
    }

    [ProviderTheory]
    [InlineData("MySql")]
    [InlineData("SqlServer")]
    public async Task StoredProcedure_BudgetAndCancellationReleaseReader(string provider)
    {
        using var fixture = new ProviderFixture(provider);
        var name = "budget_" + Guid.NewGuid().ToString("N");
        fixture.Observer.Ado.ExecuteCommand(provider == "MySql"
            ? $"CREATE PROCEDURE {name}() SELECT 1 AS value UNION ALL SELECT 2"
            : $"CREATE PROCEDURE {name} AS SELECT 1 AS value UNION ALL SELECT 2");
        try
        {
            DatabaseRoleTests.Error(await fixture.Command.CallStoredProcedure(name, maxRows: 1), 1013);
            await fixture.AssertUsable();
            var result = DatabaseRoleTests.Success(await fixture.Command.CallStoredProcedure(name, maxRows: 3));
            Assert.Equal(2, result.GetProperty("rowCount").GetInt32());
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Command.CallStoredProcedure(name, cancellationToken: cancellation.Token));
            await fixture.AssertUsable();
        }
        finally { fixture.Observer.Ado.ExecuteCommand($"DROP PROCEDURE {name}"); }
    }

    [ProviderTheory]
    [InlineData("MySql")]
    [InlineData("SqlServer")]
    public async Task StoredProcedure_ActiveCancellationAndDeadlineReleaseConnection(string provider)
    {
        using var fixture = new ProviderFixture(provider);
        var name = "cancel_" + Guid.NewGuid().ToString("N");
        fixture.Observer.Ado.ExecuteCommand(provider == "MySql"
            // MySQL can omit comments from PROCESSLIST.INFO for stored routine statements.
            ? $"CREATE PROCEDURE {name}() SELECT SLEEP(30) + 1 AS value, '{name}' AS marker"
            : $"CREATE PROCEDURE {name} AS WAITFOR DELAY '00:00:30'; SELECT 1 AS value");
        Exception? failure = null;
        using var cancellation = new CancellationTokenSource();
        Task<string>? pending = null;
        try
        {
            pending = fixture.Command.CallStoredProcedure(name, cancellationToken: cancellation.Token);
            await fixture.WaitForActivity(name, active: true);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(10)));
            await fixture.WaitForActivity(name, active: false);
            await fixture.AssertUsable();
            // MySQL SLEEP may return a normal row after native timeout cancellation.
            // Repeat on warm connections to exercise the competing timer boundary.
            for (var attempt = 0; attempt < (provider == "MySql" ? 5 : 1); attempt++)
            {
                DatabaseRoleTests.Error(await fixture.Command.CallStoredProcedure(name, commandTimeoutSeconds: 1).WaitAsync(TimeSpan.FromSeconds(10)), 1011);
                await fixture.WaitForActivity(name, active: false);
                await fixture.AssertUsable();
            }
        }
        catch (Exception ex)
        {
            failure = ex;
            output.WriteLine("Primary failure: " + ex);
            throw;
        }
        finally
        {
            cancellation.Cancel();
            try
            {
                if (pending != null)
                    try { await pending.WaitAsync(TimeSpan.FromSeconds(35)); }
                    catch (OperationCanceledException) { }
                fixture.Observer.Ado.ExecuteCommand($"DROP PROCEDURE {name}");
            }
            catch (Exception ex) when (failure != null) { output.WriteLine("Cleanup failure: " + ex); }
        }
    }
}
