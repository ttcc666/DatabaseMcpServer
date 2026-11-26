using System.Text.Json;
using System.Text.Json.Nodes;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Operations;
using DatabaseMcpServer.Services;
using DatabaseMcpServer.Tools;
using DatabaseMcpServer.Tools.Management;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;

namespace DatabaseMcpServer.Tests;

public class CapabilityConsistencyTests
{
    public static IEnumerable<object[]> UnsupportedTools => ToolContract.SqliteUnsupported.Select(n => new object[] { n });

    [Theory]
    [MemberData(nameof(UnsupportedTools))]
    public void SqliteUnsupported_HiddenAndRejectedEvenInMixedCatalog(string name)
    {
        using var fixture = DatabaseRoleTests.Create();
        using var policy = new ToolExposurePolicy();
        var catalog = new ToolCatalog();
        var options = fixture.Host.Services.GetRequiredService<DatabaseExecutionOptions>();
        Assert.DoesNotContain(name, policy.Select(catalog, fixture.Service, options));
        var tool = catalog.Tools.Single(t => t.Name == name);
        Assert.Empty(tool.SupportedProviders);
        Assert.Contains("Sqlite", tool.UnsupportedProviders);
        Assert.Contains("MongoDb", tool.UnsupportedProviders);
        var config = JsonNode.Parse(File.ReadAllText(fixture.Path))!;
        config["Databases"]!.AsArray().Add(JsonNode.Parse("""{"name":"sql","dbType":"SqlServer","connectionString":"Server=127.0.0.1;Database=unused","accessMode":"schema-admin"}"""));
        File.WriteAllText(fixture.Path, config.ToJsonString());
        Assert.True(fixture.Service.ReloadConfiguration().Success);
        Assert.Contains(name, policy.Select(catalog, fixture.Service, options));
        var schema = fixture.Host.Services.GetRequiredService<SchemaReadOperations>();
        var commands = fixture.Host.Services.GetRequiredService<SchemaCommandOperations>();
        var result = name switch
        {
            "get_data_base_list" => schema.GetDataBaseList("primary"),
            "is_any_constraint" => schema.IsAnyConstraint("missing", "primary"),
            "rename_column" => commands.RenameColumn("missing", "old", "new", "primary"),
            "add_primary_key" => commands.AddPrimaryKey("missing", "id", "primary"),
            "drop_constraint" => commands.DropConstraint("missing", "pk", "primary"),
            "add_default_value" => commands.AddDefaultValue("missing", "id", "0", "primary"),
            "add_table_remark" => commands.AddTableRemark("missing", "comment", "primary"),
            "is_any_table_remark" => schema.IsAnyTableRemark("missing", "primary"),
            "delete_table_remark" => commands.DeleteTableRemark("missing", "primary"),
            "add_column_remark" => commands.AddColumnRemark("missing", "id", "comment", "primary"),
            "delete_column_remark" => commands.DeleteColumnRemark("missing", "id", "primary"),
            _ => throw new InvalidOperationException(name)
        };
        var json = JsonSerializer.SerializeToElement(result, JsonSerializationDefaults.CompactCamelCase);
        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Equal(1012, json.GetProperty("errorCode").GetInt32());
        Assert.Equal("capability", json.GetProperty("phase").GetString());
        Assert.Equal(catalog.Tools.Single(t => t.Name == name).Capability.ToString(), json.GetProperty("capability").GetString());
    }

    [Fact]
    public async Task OfflineDetails_MatchSchemaExecution()
    {
        using var fixture = DatabaseRoleTests.Create();
        var report = DatabaseRoleTests.Success(await fixture.Host.Services.GetRequiredService<ConnectionTools>().GetDatabaseCapabilities());
        var details = report.GetProperty("capabilityDetails");
        Assert.Equal("unsupported", details.GetProperty("schemaSections").GetProperty("comments").GetString());
        Assert.Equal("partial", details.GetProperty("schemaSections").GetProperty("constraints").GetString());
        Assert.Equal("supported", details.GetProperty("cliExport").GetProperty("jsonl").GetString());
        var schema = fixture.Host.Services.GetRequiredService<SchemaReadOperations>();
        var data = JsonSerializer.SerializeToElement(await schema.GetTableSchema("markers", sections: ["comments", "constraints"]), JsonSerializationDefaults.CompactCamelCase);
        Assert.True(data.GetProperty("success").GetBoolean(), data.ToString());
        Assert.Equal("unsupported", data.GetProperty("data").GetProperty("comments").GetProperty("status").GetString());
        Assert.Equal("partial", data.GetProperty("data").GetProperty("constraints").GetProperty("status").GetString());
    }

    [Fact]
    public void Details_DistinguishUnverifiedProductsAndDriverCapabilities()
    {
        Assert.Equal("provider-dependent-unverified", ProviderCapabilities.Details(DbType.DuckDB)["cliExport"]["csv"]);
        Assert.Equal("supported", ProviderCapabilities.Details(DbType.DuckDB)["schemaSections"]["columns"]);
        Assert.Equal("unsupported", ProviderCapabilities.Details(DbType.DuckDB)["schemaSections"]["foreignKeys"]);
        Assert.Equal("unsupported", ProviderCapabilities.Details(DbType.MongoDb)["cliExport"]["jsonl"]);
        Assert.Equal("provider-dependent", ProviderCapabilities.Details(DbType.Tidb)["schemaSections"]["columns"]);
        Assert.Equal("unsupported", ProviderCapabilities.Details(DbType.Tidb)["schemaSections"]["comments"]);
        Assert.Equal("unsupported", ProviderCapabilities.Details(DbType.MongoDb)["schemaSections"]["columns"]);
        var compatible = JsonSerializer.SerializeToElement(DatabaseCapabilities.Describe(new(new(), "compatible") { DatabaseType = "Tidb" }, new(),
            new ConnectionConfig { DbType = DbType.MySql, ConnectionString = "Server=unused", MoreSettings = new() }));
        Assert.Equal("unverified", compatible.GetProperty("productVerification").GetString());
        Assert.Equal("unsupported", compatible.GetProperty("capabilities").GetProperty("boundedMultipleResults").GetString());
    }

    [Fact]
    public void PostgreSqlOptions_ReportExplicitCancellationTimeoutWithoutSecrets()
    {
        var report = JsonSerializer.SerializeToElement(DatabaseCapabilities.DriverOptions(DbType.PostgreSQL,
            "Host=unused;Password=hidden-secret;Cancellation Timeout=1700"));
        Assert.Equal(1700, report.GetProperty("cancellationTimeoutMs").GetInt32());
        Assert.Equal("connection-string", report.GetProperty("sources").GetProperty("Cancellation Timeout").GetString());
        Assert.DoesNotContain("hidden-secret", report.ToString());
    }

    [Theory]
    [InlineData("Tidb", "MySql", "Server=127.0.0.1;Port=1;Database=unused")]
    [InlineData("Vastbase", "PostgreSQL", "Host=127.0.0.1;Port=1;Database=unused")]
    public async Task CompatibleProduct_RejectsHiddenNativeToolsBeforeOpeningConnection(string product, string driver, string connectionString)
    {
        using var fixture = DatabaseRoleTests.Create();
        var config = new DatabaseMcpServer.Configuration.ConfigurationFileStore().Load(fixture.Path);
        config.Databases = [new() { Name = "compatible", DbType = product, ConnectionString = connectionString, IsDefault = true, EnableDangerousOperations = true }];
        File.WriteAllText(fixture.Path, JsonSerializer.Serialize(config));
        Assert.True(fixture.Service.ReloadConfiguration().Success);
        var report = DatabaseRoleTests.Success(await fixture.Host.Services.GetRequiredService<ConnectionTools>().GetDatabaseCapabilities());
        Assert.Equal(driver, report.GetProperty("actualProvider").GetString());
        Assert.Equal("unsupported", report.GetProperty("capabilities").GetProperty("tablePagination").GetString());
        using var policy = new ToolExposurePolicy();
        var selected = policy.Select(new ToolCatalog(), fixture.Service, new());
        Assert.Contains("get_table_schema", selected);
        Assert.Equal("provider-dependent", report.GetProperty("capabilityDetails").GetProperty("schemaSections").GetProperty("columns").GetString());
        var schema = fixture.Host.Services.GetRequiredService<DatabaseMcpServer.Tools.Management.SchemaTools>();
        var detail = DatabaseRoleTests.Success(await schema.GetTableSchema("missing", sections: ["comments"])).GetProperty("data");
        Assert.Equal("unsupported", detail.GetProperty("comments").GetProperty("status").GetString());
        DatabaseRoleTests.Error(await schema.GetTableSchema("invalid table"), 1004);
        foreach (var name in new[] { "query_table_page", "get_data_set_all", "get_object_definition", "explain_query", "call_stored_procedure" })
            Assert.DoesNotContain(name, selected);
        var query = fixture.Host.Services.GetRequiredService<DatabaseMcpServer.Tools.Query.QueryTools>();
        DatabaseRoleTests.Error(await query.QueryTablePage("missing"), 1012);
        DatabaseRoleTests.Error(await query.GetDataSetAll("SELECT 1"), 1012);
        var operations = new Task<object>[]
        {
            fixture.Host.Services.GetRequiredService<ObjectMetadataService>().GetDefinition("table", "missing", null, null, null, null, default),
            fixture.Host.Services.GetRequiredService<SqlExplainService>().Explain("SELECT 1", null, null, null, null, default)
        };
        foreach (var operation in operations)
        {
            var failure = JsonSerializer.SerializeToElement(await operation, JsonSerializationDefaults.CompactCamelCase);
            Assert.Equal(1012, failure.GetProperty("errorCode").GetInt32());
            Assert.Equal("capability", failure.GetProperty("phase").GetString());
        }
        DatabaseRoleTests.Error(await fixture.Host.Services.GetRequiredService<DatabaseMcpServer.Tools.Command.CommandTools>().CallStoredProcedure("missing"), 1012);
        DatabaseRoleTests.Error(await fixture.Host.Services.GetRequiredService<DatabaseMcpServer.Tools.Command.CommandTools>().BatchExecuteCommands(
            [new() { Sql = "INSERT INTO missing VALUES (1)" }], atomic: true), 1012);
    }

    [Fact]
    public async Task NonOltpProviders_RejectExcludedSchemaAndRoutineToolsByProductBeforeConnecting()
    {
        using var fixture = DatabaseRoleTests.Create();
        var config = new DatabaseMcpServer.Configuration.ConfigurationFileStore().Load(fixture.Path);
        config.Databases =
        [
            new() { Name = "quest", DbType = "QuestDB", ConnectionString = "host=127.0.0.1;port=1;username=u;password=p;database=qdb", IsDefault = true, AccessMode = "schema-admin" },
            new() { Name = "doris", DbType = "Doris", ConnectionString = "Server=127.0.0.1;Port=1;Database=db;Uid=u;Pwd=p;Pooling=false", AccessMode = "schema-admin" },
            new() { Name = "gbase", DbType = "GBase", ConnectionString = "Host=127.0.0.1;Service=1;Server=g;Database=d;Protocol=onsoctcp;Uid=u;Pwd=p", AccessMode = "schema-admin" }
        ];
        File.WriteAllText(fixture.Path, JsonSerializer.Serialize(config));
        Assert.True(fixture.Service.ReloadConfiguration().Success);
        var schema = fixture.Host.Services.GetRequiredService<SchemaReadOperations>();
        var commands = fixture.Host.Services.GetRequiredService<SchemaCommandOperations>();
        foreach (var result in new[]
        {
            schema.GetDataBaseList("quest"),
            schema.IsAnyConstraint("c", "quest"),
            commands.AddPrimaryKey("t", "id", "quest"),
            commands.DropProc("p", "doris"),
            commands.AddTableRemark("t", "note", "doris"),
            commands.RenameColumn("t", "a", "b", "gbase")
        })
        {
            var json = JsonSerializer.SerializeToElement(result, JsonSerializationDefaults.CompactCamelCase);
            Assert.False(json.GetProperty("success").GetBoolean());
            Assert.Equal(1012, json.GetProperty("errorCode").GetInt32());
        }
    }

    [Fact]
    public async Task DuckDbSchemaTools_ExposeAndExecuteSupportedDdlOnly()
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        var config = new DatabaseMcpServer.Configuration.ConfigurationFileStore().Load(fixture.Path);
        config.Databases =
        [
            new()
            {
                Name = "duck", DbType = "DuckDB", IsDefault = true, AccessMode = "schema-admin", EnableDangerousOperations = true,
                ConnectionString = $"DataSource={Path.Combine(fixture.Directory.FullName, "schema.duckdb")}"
            }
        ];
        File.WriteAllText(fixture.Path, JsonSerializer.Serialize(config));
        Assert.True(fixture.Service.ReloadConfiguration().Success);

        using var policy = new ToolExposurePolicy();
        var catalog = new ToolCatalog();
        var selected = policy.Select(catalog, fixture.Service, fixture.Host.Services.GetRequiredService<DatabaseExecutionOptions>());
        Assert.Contains("rename_column", selected);
        Assert.Contains("add_primary_key", selected);
        Assert.DoesNotContain("drop_constraint", selected);
        var report = DatabaseRoleTests.Success(await fixture.Host.Services.GetRequiredService<ConnectionTools>().GetDatabaseCapabilities("duck"));
        var operations = report.GetProperty("capabilityDetails").GetProperty("schemaOperations");
        Assert.Equal("provider-dependent", operations.GetProperty("RenameColumn").GetString());
        Assert.Equal("provider-dependent", operations.GetProperty("AddPrimaryKey").GetString());
        Assert.Equal("unsupported", operations.GetProperty("DropConstraint").GetString());

        using (var lease = fixture.Sessions.AcquireClient("duck"))
        {
            lease.Client.Ado.ExecuteCommand("CREATE TABLE schema_probe(id INTEGER, old_label VARCHAR)");
            lease.Client.Ado.ExecuteCommand("INSERT INTO schema_probe VALUES (1, 'preserved')");
        }
        var schema = fixture.Host.Services.GetRequiredService<SchemaTools>();
        DatabaseRoleTests.Success(schema.RenameColumn("schema_probe", "old_label", "new_label", "duck"));
        DatabaseRoleTests.Success(schema.AddPrimaryKey("schema_probe", "id", "duck"));
        var rejected = schema.DropConstraint("schema_probe", "schema_probe_id_pkey", "duck");
        DatabaseRoleTests.Error(rejected, 1012);
        var failure = JsonSerializer.Deserialize<JsonElement>(rejected);
        Assert.Equal("capability", failure.GetProperty("phase").GetString());
        Assert.Equal("DropConstraint", failure.GetProperty("capability").GetString());
        using var observer = fixture.Sessions.AcquireClient("duck");
        Assert.Equal("preserved", observer.Client.Ado.GetString("SELECT new_label FROM schema_probe WHERE id = 1"));
        Assert.Equal(1, observer.Client.Ado.GetInt("SELECT COUNT(*) FROM duckdb_constraints() WHERE table_name = 'schema_probe' AND constraint_type = 'PRIMARY KEY'"));
    }

    [Fact]
    public void QuerySchemas_DescribeJsonObjectsWithoutRawBufferDetails()
    {
        var catalog = new ToolCatalog();
        foreach (var (name, property) in new[] { ("query_database", "data"), ("explain_query", "plan") })
        {
            var schema = catalog.Tools.Single(t => t.Name == name).ProtocolTool.OutputSchema!.Value;
            var items = schema.GetProperty("properties").GetProperty(property).GetProperty("items");
            Assert.Equal("object", items.GetProperty("type").GetString());
            Assert.DoesNotContain("utf8Json", schema.ToString());
        }
    }

    [Fact]
    public async Task DriverMaintenanceGaps_RejectUnsupportedProductsWith1012BeforeConnecting()
    {
        using var fixture = DatabaseRoleTests.Create();
        var config = new DatabaseMcpServer.Configuration.ConfigurationFileStore().Load(fixture.Path);
        config.Databases =
        [
            new() { Name = "mysql", DbType = "MySql", ConnectionString = "Server=127.0.0.1;Port=1;Database=t;Uid=u;Pwd=p;", IsDefault = true, AccessMode = "schema-admin" },
            new() { Name = "pg", DbType = "PostgreSQL", ConnectionString = "Host=127.0.0.1;Port=1;Database=t;Username=u;Password=p;", AccessMode = "schema-admin" },
            new() { Name = "oracle", DbType = "Oracle", ConnectionString = "Data Source=127.0.0.1:1/orcl;User Id=u;Password=p;", AccessMode = "schema-admin" },
            new() { Name = "dm", DbType = "Dm", ConnectionString = "Server=127.0.0.1:1;User Id=u;PWD=p;DATABASE=t;", AccessMode = "schema-admin" },
            new() { Name = "gbase", DbType = "GBase", ConnectionString = "Host=127.0.0.1;Service=1;Server=g;Database=t;Protocol=onsoctcp;Uid=u;Pwd=p;", AccessMode = "schema-admin" },
            new() { Name = "ob_oracle", DbType = "OceanBaseForOracle", ConnectionString = "Driver={ODBC};Server=127.0.0.1;Port=1;Database=t;User=u;Password=p;", AccessMode = "schema-admin" },
            new() { Name = "ch", DbType = "ClickHouse", ConnectionString = "Host=127.0.0.1;Port=1;Database=t;User=default;Password=p;", AccessMode = "schema-admin" },
            new() { Name = "oscar", DbType = "Oscar", ConnectionString = "HOST=127.0.0.1;PORT=1;DATABASE=osrdb;USER ID=SYSDBA;PASSWORD=oscar;", AccessMode = "schema-admin" },
            new() { Name = "quest", DbType = "QuestDB", ConnectionString = "host=127.0.0.1;port=1;username=u;password=p;database=t;", AccessMode = "schema-admin" },
            new() { Name = "duck", DbType = "DuckDB", ConnectionString = $"DataSource={Path.Combine(fixture.Directory.FullName, "cap.duckdb")}", AccessMode = "schema-admin" }
        ];
        File.WriteAllText(fixture.Path, JsonSerializer.Serialize(config));
        Assert.True(fixture.Service.ReloadConfiguration().Success);

        var schema = fixture.Host.Services.GetRequiredService<SchemaReadOperations>();
        var commands = fixture.Host.Services.GetRequiredService<SchemaCommandOperations>();
        var metadata = fixture.Host.Services.GetRequiredService<ObjectMetadataService>();

        foreach (var result in new[]
        {
            schema.IsAnyTableRemark("t", "mysql"),
            schema.IsAnyTableRemark("t", "pg"),
            schema.IsAnyTableRemark("t", "duck"),
            commands.DeleteColumnRemark("t", "c", "mysql"),
            commands.DeleteColumnRemark("t", "c", "ch"),
            schema.GetDataBaseList("oracle"),
            schema.GetDataBaseList("dm"),
            schema.GetDataBaseList("gbase"),
            schema.GetDataBaseList("ob_oracle"),
            commands.RenameTable("t1", "t2", "ch"),
            commands.RenameTable("t1", "t2", "oscar"),
            commands.RenameTable("t1", "t2", "quest"),
            commands.RenameTable("t1", "t2", "gbase"),
            commands.DropIndex("t", "ix", "ch"),
            commands.DropIndex("t", "ix", "quest"),
            await metadata.ListObjects("procedure", "duck", null, null, null, null, 10, null, null, default),
            await metadata.ListObjects("function", "ch", null, null, null, null, 10, null, null, default)
        })
        {
            var json = JsonSerializer.SerializeToElement(result, JsonSerializationDefaults.CompactCamelCase);
            Assert.False(json.GetProperty("success").GetBoolean(), json.ToString());
            Assert.Equal(1012, json.GetProperty("errorCode").GetInt32());
            Assert.Equal("capability", json.GetProperty("phase").GetString());
        }
    }

    [Fact]
    public void OceanBaseOracle_MaintenanceToolsStayAvailableAndUseDriverSql()
    {
        var statements = new List<string>();
        using var fixture = new RuntimeConsistencyTests.Fixture(observeClient: db =>
        {
            db.CurrentConnectionConfig.IsAutoCloseConnection = false;
            db.CurrentConnectionConfig.SqlMiddle = new SqlMiddle
            {
                IsSqlMiddle = true,
                ExecuteCommand = (sql, _) => { statements.Add(sql); return 0; },
                GetDataSetAll = (sql, _) =>
                {
                    statements.Add(sql);
                    var data = new System.Data.DataSet();
                    var table = data.Tables.Add();
                    table.Columns.Add("COMMENTS");
                    table.Rows.Add("description");
                    return data;
                }
            };
        });
        var config = new DatabaseMcpServer.Configuration.ConfigurationFileStore().Load(fixture.Path);
        config.Databases =
        [
            new() { Name = "ob_oracle", DbType = "OceanBaseForOracle", ConnectionString = "Driver={ODBC};Server=127.0.0.1;Port=1;Database=t;User=u;Password=p;",
                IsDefault = true, AccessMode = "schema-admin", EnableDangerousOperations = true }
        ];
        File.WriteAllText(fixture.Path, JsonSerializer.Serialize(config));
        Assert.True(fixture.Service.ReloadConfiguration().Success);

        using var policy = new ToolExposurePolicy();
        var exposed = policy.Select(new ToolCatalog(), fixture.Service, fixture.Host.Services.GetRequiredService<DatabaseExecutionOptions>());
        foreach (var name in new[] { "rename_table", "add_default_value", "add_table_remark", "delete_table_remark", "add_column_remark", "delete_column_remark", "is_any_table_remark" })
            Assert.Contains(name, exposed);
        var details = ProviderCapabilities.Details(DbType.OceanBaseForOracle)["schemaOperations"];
        foreach (var capability in new[] { "RenameTable", "DefaultValue", "SchemaComments", "TableRemarkLookup", "DeleteColumnRemark" })
            Assert.Equal("provider-dependent", details[capability]);
        Assert.DoesNotContain("get_data_base_list", exposed);
        Assert.Equal("unsupported", details["DatabaseListing"]);

        var tools = fixture.Host.Services.GetRequiredService<SchemaTools>();
        DatabaseRoleTests.Success(tools.RenameTable("T1", "T2"));
        DatabaseRoleTests.Success(tools.AddDefaultValue("T", "C", "0"));
        DatabaseRoleTests.Success(tools.AddTableRemark("T", "description"));
        DatabaseRoleTests.Success(tools.DeleteTableRemark("T"));
        DatabaseRoleTests.Success(tools.AddColumnRemark("T", "C", "description"));
        DatabaseRoleTests.Success(tools.DeleteColumnRemark("T", "C"));
        Assert.True(DatabaseRoleTests.Success(tools.IsAnyTableRemark("T")).GetProperty("exists").GetBoolean());
        Assert.Equal(new[]
        {
            "alter table T1 rename to T2",
            "ALTER TABLE T MODIFY(C DEFAULT '0')",
            "comment on table T  is  'description'",
            "comment on table T  is  ''",
            "comment on column T.C is 'description'",
            "comment on column T.C is ''",
            "select * from user_tab_comments where Table_Name='T'order by Table_Name"
        }, statements);
    }
}
