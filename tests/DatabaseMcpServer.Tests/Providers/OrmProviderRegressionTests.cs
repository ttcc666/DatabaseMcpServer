using DatabaseMcpServer.Configuration;
using System.Text.Json;
using DatabaseMcpServer.Cli;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using DatabaseMcpServer.Strategies.DBSetting;
using DatabaseMcpServer.Tools.Command;
using DatabaseMcpServer.Tools.Management;
using DatabaseMcpServer.Tools.Query;
using DatabaseMcpServer.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;

namespace DatabaseMcpServer.Tests;

public class OrmProviderRegressionTests
{
    [Fact]
    public async Task MetadataTools_IsolateSameNamedTablesAndRefreshAfterDdl()
    {
        using var fixture = DatabaseRoleTests.Create();
        var tools = fixture.Host.Services.GetRequiredService<SchemaTools>();
        var table = "metadata_" + Guid.NewGuid().ToString("N");
        foreach (var (name, key) in new[] { ("primary", "first_id"), ("analytics", "second_id") })
        {
            using var lease = fixture.Sessions.AcquireClient(name);
            lease.Client.Ado.ExecuteCommand($"CREATE TABLE {table}({key} INTEGER PRIMARY KEY AUTOINCREMENT, old_col TEXT)");
            // Seed SqlSugar's shared cache to prove the tools bypass it.
            _ = lease.Client.DbMaintenance.GetPrimaries(table);
            _ = lease.Client.DbMaintenance.GetIsIdentities(table);
            Assert.Equal(key, Assert.Single(DatabaseRoleTests.Success(await tools.GetTableSchema(table, name, sections: ["primaryKeys"])).GetProperty("data").GetProperty("primaryKeys").EnumerateArray()).GetString());
            Assert.Equal(key, Assert.Single(DatabaseRoleTests.Success(await tools.GetTableSchema(table, name, sections: ["identityColumns"])).GetProperty("data").GetProperty("identityColumns").EnumerateArray()).GetString());
        }
        Assert.Empty(DatabaseRoleTests.Success(await fixture.Host.Services.GetRequiredService<ObjectMetadataTools>().ListObjects("view", "analytics").Json()).GetProperty("objects").EnumerateArray());
        Assert.True(DatabaseRoleTests.Success(tools.IsAnyColumn(table, "old_col", "analytics")).GetProperty("exists").GetBoolean());
        using (var lease = fixture.Sessions.AcquireClient("analytics"))
        {
            _ = lease.Client.DbMaintenance.GetViewInfoList();
            _ = lease.Client.DbMaintenance.IsAnyColumn(table, "old_col");
            lease.Client.Ado.ExecuteCommand($"create view {table}_view as select second_id from {table}");
        }
        Assert.Single(DatabaseRoleTests.Success(await fixture.Host.Services.GetRequiredService<ObjectMetadataTools>().ListObjects("view", "analytics").Json()).GetProperty("objects").EnumerateArray());
        DatabaseRoleTests.Success(tools.DropColumn(table, "old_col", "analytics"));
        Assert.False(DatabaseRoleTests.Success(tools.IsAnyColumn(table, "old_col", "analytics")).GetProperty("exists").GetBoolean());
        Assert.True(DatabaseRoleTests.Success(tools.IsAnyColumn(table, "old_col", "primary")).GetProperty("exists").GetBoolean());
    }

    [Fact]
    public async Task MetadataAndDdl_DistinguishRequestDeadlineFromNativeTimeout()
    {
        var timeouts = new List<int>();
        using var fixture = new RuntimeConsistencyTests.Fixture(
            observeClient: db => db.Aop.OnLogExecuting = (_, _) => timeouts.Add(db.Ado.CommandTimeOut),
            configureServices: services => services.AddSingleton(new DatabaseExecutionOptions { MaxCommandTimeoutSeconds = 7 }));
        var tools = fixture.Host.Services.GetRequiredService<SchemaTools>();
        DatabaseRoleTests.Success(await tools.GetTableSchema("sqlite_master"));
        Assert.NotEmpty(timeouts);
        Assert.All(timeouts, timeout => Assert.Equal(8, timeout)); // 7s request deadline, later native fallback.
        timeouts.Clear();
        DatabaseRoleTests.Success(tools.CreateTable("timeout_probe", [new() { Name = "id", DataType = "integer" }]));
        Assert.NotEmpty(timeouts);
        Assert.All(timeouts, timeout => Assert.Equal(7, timeout)); // Synchronous DDL retains native timeout semantics.
        timeouts.Clear();
        DatabaseRoleTests.Success(await tools.GetTableSchema("sqlite_master", refresh: true));
        Assert.NotEmpty(timeouts);
        Assert.All(timeouts, timeout => Assert.Equal(8, timeout));
    }

    public static IEnumerable<object[]> Presets() => DatabasePresetCatalog.Presets.Select(p => new object[] { p.DbType });

    [Theory]
    [MemberData(nameof(Presets))]
    public void Presets_LoadBundledProvidersWithoutOpeningConnections(string dbType)
    {
        Assert.True(DatabasePresetCatalog.TryGet(dbType, out var preset));
        using var factory = Factory();
        var connection = Connection(preset);
        Assert.Equal("available", preset.Availability);
        Assert.Null(preset.UnavailableReason);
        Assert.Empty(DatabaseConfigurationValidator.Validate(new() { Databases = [connection] }).Errors);
        using var lease = factory.AcquireClient(connection);
        // These constructors require ClientSDK; TDengine also opens the remote connection immediately.
        if (dbType is "GBase" or "TDengine")
        {
            var driver = dbType == "GBase" ? typeof(GBS.Data.GBasedbt.GbsConnection) : typeof(SqlSugar.TDengineAdo.TDengineConnection);
            Assert.True(typeof(System.Data.Common.DbConnection).IsAssignableFrom(driver));
            var capabilities = JsonSerializer.SerializeToElement(DatabaseCapabilities.Describe(lease.Context, new(), lease.Client.CurrentConnectionConfig));
            Assert.Equal(dbType, capabilities.GetProperty("actualProvider").GetString());
            Assert.Equal("unverified", capabilities.GetProperty("productVerification").GetString());
            return;
        }
        Assert.NotNull(lease.Client.Ado.Connection);
        Assert.Equal(System.Data.ConnectionState.Closed, lease.Client.Ado.Connection.State);
    }

    [Fact]
    public void BundledExtensions_RegisterTogetherWithoutPoisoningMongo()
    {
        using var factory = Factory();
        foreach (var dbType in new[] { "HG", "GBase", "TDengine", "DuckDB" })
        {
            Assert.True(DatabasePresetCatalog.TryGet(dbType, out var preset));
            using var lease = factory.AcquireClient(Connection(preset));
            Assert.Equal(dbType, lease.Client.CurrentConnectionConfig.DbType.ToString());
            Assert.Contains(InstanceFactory.CustomAssemblies, assembly => assembly.GetName().Name == "SqlSugar." + dbType + "Core");
        }
        Assert.True(DatabasePresetCatalog.TryGet("MongoDb", out var installed));
        using var mongo = factory.AcquireClient(Connection(installed));
        Assert.Equal(SqlSugar.DbType.MongoDb, mongo.Client.CurrentConnectionConfig.DbType);
        Assert.Contains(InstanceFactory.CustomAssemblies, assembly => assembly.GetName().Name == "SqlSugar.MongoDbCore");
    }

    [Fact]
    public async Task BundledExtensionConfiguration_ReloadsAndReportsCapabilitiesOffline()
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        var config = new ConfigurationFileStore().Load(fixture.Path);
        config.Databases.Add(new() { Name = "highgo", DbType = "HG", ConnectionString = "Server=localhost;Port=1;Database=test" });
        var validation = DatabaseConfigurationValidator.Validate(config);
        Assert.Empty(validation.Errors);
        Assert.Empty(validation.Warnings);
        File.WriteAllText(fixture.Path, JsonSerializer.Serialize(config));
        Assert.True(fixture.Service.ReloadConfiguration().Success);
        var queries = fixture.Host.Services.GetRequiredService<QueryTools>();
        var capabilities = DatabaseRoleTests.Success(await fixture.Host.Services.GetRequiredService<ConnectionTools>().GetDatabaseCapabilities("highgo"));
        Assert.Equal("HG", capabilities.GetProperty("actualProvider").GetString());
        Assert.Equal("unverified", capabilities.GetProperty("productVerification").GetString());
        Assert.Equal(1, DatabaseRoleTests.Success(await fixture.Host.Services.GetRequiredService<DatabaseQueryTools>().QueryDatabase("select 1 as value", databaseName: "primary").Json()).GetProperty("data")[0].GetProperty("value").GetInt32());
    }

    [Fact]
    public async Task DuckDbTools_PersistTypedValuesAcrossLeasesAndRefreshSchema()
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        var config = new ConfigurationFileStore().Load(fixture.Path);
        config.Databases.Add(new()
        {
            Name = "duck", DbType = "DuckDB", EnableDangerousOperations = true,
            ConnectionString = $"DataSource={System.IO.Path.Combine(fixture.Directory.FullName, "test.duckdb")}"
        });
        File.WriteAllText(fixture.Path, JsonSerializer.Serialize(config));
        Assert.True(fixture.Service.ReloadConfiguration().Success);
        var commands = fixture.Host.Services.GetRequiredService<CommandTools>();
        var queries = fixture.Host.Services.GetRequiredService<DatabaseQueryTools>();
        var schema = fixture.Host.Services.GetRequiredService<SchemaTools>();
        DatabaseRoleTests.Success(await commands.ExecuteCommand("create table driver_probe(id bigint primary key, label varchar, amount decimal(18,4))", databaseName: "duck"));
        DatabaseRoleTests.Success(await commands.ExecuteCommand("insert into driver_probe values (@id, @label, @amount)",
            [new() { Name = "id", DbType = DatabaseParameterType.Int64, Value = JsonSerializer.SerializeToElement("9007199254740993") },
             new() { Name = "label", DbType = DatabaseParameterType.String, Value = JsonSerializer.SerializeToElement("驱动 O'Reilly") },
             new() { Name = "amount", DbType = DatabaseParameterType.Decimal, Value = JsonSerializer.SerializeToElement("12345678.1250") }], databaseName: "duck"));
        var result = DatabaseRoleTests.Success(await queries.QueryDatabase("select id, label, amount from driver_probe", databaseName: "duck").Json());
        var row = Assert.Single(result.GetProperty("data").EnumerateArray());
        Assert.Equal(9007199254740993L, row.GetProperty("id").GetInt64());
        Assert.Equal("驱动 O'Reilly", row.GetProperty("label").GetString());
        Assert.Equal(12345678.1250m, row.GetProperty("amount").GetDecimal());
        var initial = DatabaseRoleTests.Success(await schema.GetTableSchema("driver_probe", "duck", refresh: true));
        Assert.Contains("label", initial.GetRawText());
        var listed = DatabaseRoleTests.Success(await fixture.Host.Services.GetRequiredService<ObjectMetadataTools>()
            .ListObjects("table", "duck", namePrefix: "driver_probe").Json());
        Assert.Equal("client", listed.GetProperty("pagingMode").GetString());
        Assert.Equal("driver_probe", Assert.Single(listed.GetProperty("objects").EnumerateArray()).GetProperty("name").GetString());
        DatabaseRoleTests.Success(await commands.ExecuteCommand("alter table driver_probe add column extra integer", databaseName: "duck"));
        var refreshed = DatabaseRoleTests.Success(await schema.GetTableSchema("driver_probe", "duck", refresh: true));
        Assert.Contains("extra", refreshed.GetRawText());
        DatabaseRoleTests.Success(schema.DropTable("driver_probe", "duck"));
        Assert.Empty(DatabaseRoleTests.Success(await fixture.Host.Services.GetRequiredService<ObjectMetadataTools>().ListObjects("table", "duck").Json()).GetProperty("objects").EnumerateArray());
    }

    [Fact]
    public void Configuration_ReadsScalarOptionsAndRejectsInvalidValuesWithoutSecrets()
    {
        var config = JsonSerializer.Deserialize<DatabasesConfig>("""
            {"Databases":[{"Name":"kdb","DbType":"Kdbndp","ConnectionString":"Host=localhost;User ID=SYSTEM;Password=secret",
            "OptimizationSettings":{"disableNvarchar":true,"maxPoolSize":50,"mode":"Oracle","custom":"preserved"}}]}
            """)!;
        var db = Assert.Single(config.Databases);
        Assert.Equal("true", db.OptimizationSettings!["disableNvarchar"]);
        Assert.Equal("50", db.OptimizationSettings["maxPoolSize"]);
        Assert.Empty(DatabaseConfigurationValidator.Validate(config).Errors);
        var serialized = JsonSerializer.SerializeToElement(db);
        Assert.Equal(JsonValueKind.String, serialized.GetProperty("OptimizationSettings").GetProperty("maxPoolSize").ValueKind);
        db.ConnectionString = "Server=localhost;User=SYSTEM;Password=secret";
        db.OptimizationSettings["maxPoolSize"] = "-1";
        db.OptimizationSettings["mode"] = "typo";
        var errors = DatabaseConfigurationValidator.Validate(config).Errors;
        Assert.Equal(3, errors.Count);
        Assert.Contains(errors, e => e.Contains("maxPoolSize"));
        Assert.Contains(errors, e => e.Contains("mode"));
        Assert.DoesNotContain("secret", string.Join("\n", errors));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"x\":{}}")]
    [InlineData("{\"x\":null}")]
    public void Configuration_RejectsNonScalarOptions(string options)
        => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DatabaseConnection>("{\"OptimizationSettings\":" + options + "}"));

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("-1")]
    public void Wizard_RejectsInvalidRelationalPorts(string port)
    {
        Assert.Throws<InvalidOperationException>(() => new ConnectionStringBuilder().Build("MySql",
            new Dictionary<string, string?> { ["Server"] = "localhost", ["Port"] = port }));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("65535")]
    public void Wizard_AcceptsPortBoundariesAndUsesKdbUserId(string port)
    {
        var result = new ConnectionStringBuilder().Build("Kdbndp",
            new Dictionary<string, string?> { ["Server"] = "localhost", ["Port"] = port, ["User ID"] = "SYSTEM" });
        var parsed = new Kdbndp.KdbndpConnectionStringBuilder(result);
        Assert.Equal(int.Parse(port), parsed.Port);
        Assert.Equal("SYSTEM", parsed["User ID"]);
    }

    [Fact]
    public void OptimizationDiagnostics_ReportMappedValuesAndUnimplementedOptions()
    {
        using var factory = Factory();
        using var lease = factory.AcquireClient(new() { Name = "sqlite", DbType = "Sqlite", ConnectionString = "Data Source=:memory:",
            OptimizationSettings = new() { ["enableDefaultValue"] = "false", ["enableDescription"] = "true", ["enableBulkCopy"] = "true", ["unknown"] = "secret" } });
        var report = JsonSerializer.SerializeToElement(DatabaseCapabilities.Describe(lease.Context, new(), lease.Client.CurrentConnectionConfig));
        var options = report.GetProperty("optimizationSettings").EnumerateArray().ToDictionary(o => o.GetProperty("name").GetString()!);
        Assert.Equal("mapped", options["enableDefaultValue"].GetProperty("status").GetString());
        Assert.False(options["enableDefaultValue"].GetProperty("effective").GetBoolean());
        Assert.True(options["enableDescription"].GetProperty("effective").GetBoolean());
        Assert.Contains("CodeFirst-only", options["enableDescription"].GetProperty("scope").GetString());
        Assert.Equal("not-applicable", options["enableBulkCopy"].GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, options["enableBulkCopy"].GetProperty("effective").ValueKind);
        Assert.Equal("unknown", options["unknown"].GetProperty("status").GetString());
        Assert.DoesNotContain("secret", report.GetRawText());
    }

    [Fact]
    public void GaussModel_PreservesLegacyOptionAndRejectsConflicts()
    {
        using var factory = Factory();
        var connection = new DatabaseConnection { Name = "gauss", DbType = "GaussDBNative", ConnectionString = "Host=localhost;Database=test;User ID=user",
            OptimizationSettings = new() { ["isOpenGauss"] = "true" } };
        using (var lease = factory.AcquireClient(connection))
            Assert.Equal(DbType.OpenGauss, lease.Client.CurrentConnectionConfig.MoreSettings.DatabaseModel);
        connection.OptimizationSettings["databaseModel"] = "GaussDB";
        Assert.Single(DatabaseConfigurationValidator.Validate(new() { Databases = [connection] }).Errors);
        Assert.Throws<DatabaseMcpException>(() => factory.AcquireClient(connection));
    }

    [Fact]
    public async Task TiDbProcedureOutput_IsRejectedBeforeOpeningConnection()
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        var config = new ConfigurationFileStore().Load(fixture.Path);
        config.Databases[0].DbType = "Tidb";
        config.Databases[0].ConnectionString = "Server=localhost;Port=1;User=root;Database=test";
        File.WriteAllText(fixture.Path, JsonSerializer.Serialize(config));
        Assert.True(fixture.Service.ReloadConfiguration().Success);
        var result = await fixture.Host.Services.GetRequiredService<CommandTools>().CallStoredProcedure("unused",
            [new() { Name = "output", Direction = DatabaseParameterDirection.Output, DbType = DatabaseParameterType.Int32 }]);
        DatabaseRoleTests.Error(result, 1012);
    }

    [Theory]
    [InlineData("GaussDB", "true", "Npgsql.NpgsqlConnection")]
    [InlineData("OpenGauss", "true", "Npgsql.NpgsqlConnection")]
    [InlineData("GaussDBNative", "false", "OpenGauss.NET.OpenGaussConnection")]
    public void GaussDriver_IsSelectedByDbTypeOnly(string product, string native, string connectionType)
    {
        using var factory = Factory();
        using var lease = factory.AcquireClient(new() { Name = "gauss", DbType = product,
            ConnectionString = "Host=localhost;Database=test;User ID=user;Password=secret;No Reset On Close=true",
            OptimizationSettings = new() { ["nativeDriver"] = native, ["databaseModel"] = "OpenGauss" } });
        Assert.Equal(connectionType, lease.Client.Ado.Connection.GetType().FullName);
        if (product == "GaussDBNative") Assert.Equal(DbType.OpenGauss, lease.Client.CurrentConnectionConfig.MoreSettings.DatabaseModel);
        var diagnostic = JsonSerializer.SerializeToElement(DatabaseCapabilities.Describe(lease.Context, new(), lease.Client.CurrentConnectionConfig));
        var legacy = diagnostic.GetProperty("optimizationSettings").EnumerateArray().Single(o => o.GetProperty("name").GetString() == "nativeDriver");
        Assert.Equal("advisory", legacy.GetProperty("status").GetString());
        Assert.DoesNotContain("secret", diagnostic.GetRawText());
        if (product is "GaussDB" or "OpenGauss")
        {
            Assert.Equal("sequential-for-bounded-text-commands", diagnostic.GetProperty("fieldReading").GetProperty("readerMode").GetString());
            Assert.True(diagnostic.GetProperty("sqlSugarSettings").GetProperty("disableNvarchar").GetBoolean());
        }
    }

    [Fact]
    public void BuiltInAliasesAndUpdatedPresets_ValidateAndMapConsistently()
    {
        foreach (var dbType in new[] { "MySqlConnector", "TDSQL", "GaussDB", "Oscar", "Doris" })
            Assert.True(DatabasePresetCatalog.TryGet(dbType, out _), $"Missing preset for {dbType}");

        Assert.Contains("Port=9030", DatabasePresetCatalog.Presets.Single(p => p.DbType == "Doris").ExampleConnectionString);
        Assert.Contains("PORT=2003", DatabasePresetCatalog.Presets.Single(p => p.DbType == "Oscar").ExampleConnectionString);
        Assert.Contains("DATABASE=osrdb", DatabasePresetCatalog.Presets.Single(p => p.DbType == "Oscar").ExampleConnectionString);
        Assert.DoesNotContain("No Reset On Close", DatabasePresetCatalog.Presets.Single(p => p.DbType == "GaussDBNative").ExampleConnectionString);

        foreach (var (dbType, table, expected) in new[]
        {
            (DbType.PolarDB, "db.users", "`db`.`users`"),
            (DbType.GoldenDB, "db.users", "`db`.`users`"),
            (DbType.TDSQL, "db.users", "`db`.`users`")
        })
            Assert.Equal(expected, SqlSafetyGuard.QuoteTableName(table, dbType));

        using var factory = Factory();
        using var golden = factory.AcquireClient(new()
        {
            Name = "golden", DbType = "GoldenDB", ConnectionString = "Server=localhost;Database=test;Uid=root;Pwd=secret;Pooling=false;",
            OptimizationSettings = new() { ["disableNvarchar"] = "true", ["disablePooling"] = "true" }
        });
        Assert.True(golden.Client.CurrentConnectionConfig.MoreSettings.DisableNvarchar);
        var goldenCaps = JsonSerializer.SerializeToElement(DatabaseCapabilities.Describe(golden.Context, new(), golden.Client.CurrentConnectionConfig));
        Assert.True(goldenCaps.GetProperty("sqlSugarSettings").GetProperty("disableNvarchar").GetBoolean());
        Assert.Equal("advisory", goldenCaps.GetProperty("optimizationSettings").EnumerateArray()
            .Single(o => o.GetProperty("name").GetString() == "disablePooling").GetProperty("status").GetString());

        Assert.NotNull(DatabaseProviderCatalog.ConnectionStringError(DbType.QuestDB, "host=localhost;port=bad;password=secret"));
        Assert.DoesNotContain("secret", DatabaseProviderCatalog.ConnectionStringError(DbType.Dm, "Server=localhost;Bad\"Quote=\"") ?? "");
    }

    [Theory]
    [InlineData("Tidb", "MySql")]
    [InlineData("Vastbase", "PostgreSQL")]
    public void Capabilities_SeparateProductVerificationFromDriverAvailability(string product, string actual)
    {
        Assert.True(DatabasePresetCatalog.TryGet(product, out var preset));
        using var factory = Factory();
        using var lease = factory.AcquireClient(Connection(preset));
        var result = JsonSerializer.SerializeToElement(DatabaseCapabilities.Describe(lease.Context, new(), lease.Client.CurrentConnectionConfig));
        Assert.Equal(product, result.GetProperty("databaseType").GetString());
        Assert.Equal(actual, result.GetProperty("actualProvider").GetString());
        Assert.Equal("unverified", result.GetProperty("productVerification").GetString());
        Assert.Equal("unverified", result.GetProperty("capabilities").GetProperty("asyncQueryCancellation").GetString());
        Assert.Equal("unsupported", result.GetProperty("capabilities").GetProperty("tablePagination").GetString());
        Assert.Equal("unverified", result.GetProperty("capabilityVerification").GetProperty("tablePagination").GetString());
        Assert.Equal("unsupported", result.GetProperty("capabilities").GetProperty("typedProcedureOutput").GetString());
    }

    [Fact]
    public async Task MongoSqlTools_ReturnUnsupportedBeforeOpeningConnection()
    {
        using var fixture = new RuntimeConsistencyTests.Fixture();
        var config = new ConfigurationFileStore().Load(fixture.Path);
        config.Databases[0].DbType = "MongoDb";
        config.Databases[0].ConnectionString = "mongodb://localhost:1/test";
        File.WriteAllText(fixture.Path, JsonSerializer.Serialize(config));
        Assert.True(fixture.Service.ReloadConfiguration().Success);
        var capabilities = DatabaseRoleTests.Success(await fixture.Host.Services.GetRequiredService<ConnectionTools>().GetDatabaseCapabilities());
        Assert.Equal("unsupported", capabilities.GetProperty("capabilities").GetProperty("sqlTools").GetString());
        DatabaseRoleTests.Error(await fixture.Host.Services.GetRequiredService<DatabaseQueryTools>().QueryDatabase("select 1").Json(), 1012);
        DatabaseRoleTests.Error(await fixture.Host.Services.GetRequiredService<DatabaseQueryTools>().QueryDatabase("select 1").Json(), 1012);
        DatabaseRoleTests.Error(await fixture.Host.Services.GetRequiredService<CommandTools>().ExecuteCommand("insert into t values (1)"), 1012);
    }

    private static JsonElement Data(string result) => DatabaseRoleTests.Success(result).GetProperty("data");
    private static DatabaseConnection Connection(DatabasePreset preset) => new()
        { Name = preset.ExampleName, DbType = preset.DbType, ConnectionString = preset.ExampleConnectionString };
    private static SqlSugarClientFactory Factory() => new(NullLogger<SqlSugarClientFactory>.Instance,
        new DatabaseOptimizationStrategyFactory(), new());
}
