using DatabaseMcpServer.Gui.Core.Services;

namespace DatabaseMcpServer.Tests;

public sealed class DbTypeConnectionFieldsCatalogTests
{
    [Theory]
    [MemberData(nameof(PerformanceCatalogs))]
    public void PerformanceKeys_ShouldMatchDatabaseSettingDocument(string dbType, string[] expected)
    {
        var actual = DbTypeConnectionFields.GetPerformanceFields(dbType).Select(field => field.Key).ToArray();
        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(OptionalCatalogs))]
    public void OptionalKeys_ShouldMatchDatabaseSettingDocument(string dbType, string[] expected)
    {
        var actual = DbTypeConnectionFields.GetOptionalFields(dbType).Select(field => field.Key).ToArray();
        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(OptimizationCatalogs))]
    public void OptimizationKeys_ShouldMatchDatabaseSettingDocument(string dbType, string[] expected)
    {
        var actual = DbTypeConnectionFields.GetOptimizationFields(dbType).Select(field => field.Key).ToArray();
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("MySql")]
    [InlineData("PostgreSQL")]
    [InlineData("SqlServer")]
    [InlineData("Oracle")]
    [InlineData("GaussDB")]
    [InlineData("Tidb")]
    public void OptionalKeys_ShouldNotDuplicateRequiredKeys(string dbType)
    {
        var required = DbTypeConnectionFields.GetFields(dbType)
            .Select(field => field.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var overlap = DbTypeConnectionFields.GetPerformanceFields(dbType)
            .Concat(DbTypeConnectionFields.GetOptionalFields(dbType))
            .Select(field => field.Key)
            .Where(required.Contains)
            .ToArray();
        Assert.Empty(overlap);
    }

    [Theory]
    [InlineData("MySql")]
    [InlineData("PostgreSQL")]
    [InlineData("SqlServer")]
    [InlineData("Sqlite")]
    [InlineData("GaussDB")]
    public void PerformanceAndOptionalKeys_ShouldNotOverlap(string dbType)
    {
        var performance = DbTypeConnectionFields.GetPerformanceFields(dbType)
            .Select(field => field.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var overlap = DbTypeConnectionFields.GetOptionalFields(dbType)
            .Select(field => field.Key)
            .Where(performance.Contains)
            .ToArray();
        Assert.Empty(overlap);
    }

    public static TheoryData<string, string[]> PerformanceCatalogs() => new()
    {
        {
            "MySql",
            [
                "Charset", "AllowLoadLocalInfile", "Pooling", "Min Pool Size", "Max Pool Size",
                "Allow User Variables", "Connection Timeout"
            ]
        },
        {
            "PolarDB",
            [
                "Charset", "AllowLoadLocalInfile", "Pooling", "Min Pool Size", "Max Pool Size",
                "Allow User Variables", "Connection Timeout"
            ]
        },
        {
            "Tidb",
            [
                "Charset", "AllowLoadLocalInfile", "Pooling", "Min Pool Size", "Max Pool Size",
                "Connection Timeout"
            ]
        },
        {
            "OceanBase",
            ["Charset", "Pooling", "Min Pool Size", "Max Pool Size", "Connection Timeout"]
        },
        {
            "PostgreSQL",
            [
                "Pooling", "Minimum Pool Size", "Maximum Pool Size", "Timeout", "Command Timeout",
                "Search Path", "MaxPoolSize"
            ]
        },
        {
            "GaussDB",
            [
                "Pooling", "Minimum Pool Size", "Maximum Pool Size", "No Reset On Close",
                "Connection Timeout", "Command Timeout"
            ]
        },
        {
            "OpenGauss",
            [
                "Pooling", "Minimum Pool Size", "Maximum Pool Size", "No Reset On Close",
                "Connection Timeout", "Command Timeout"
            ]
        },
        {
            "GaussDBNative",
            [
                "Pooling", "Minimum Pool Size", "Maximum Pool Size", "No Reset On Close",
                "Connection Timeout", "Command Timeout"
            ]
        },
        {
            "SqlServer",
            ["Min Pool Size", "Max Pool Size", "Connection Timeout", "Pooling"]
        },
        {
            "Oracle",
            ["Pooling", "Min Pool Size", "Max Pool Size", "Connection Timeout"]
        },
        {
            "Sqlite",
            ["Cache", "Mode", "Journal Mode", "Synchronous", "Foreign Keys"]
        },
        {
            "Dm",
            ["SCHEMA", "Pooling", "Min Pool Size", "Max Pool Size", "Connection Timeout", "DatabaseModel"]
        },
        { "GoldenDB", ["Pooling"] },
        { "HG", ["searchpath", "Pooling"] },
        {
            "Kdbndp",
            [
                "Pooling", "Minimum Pool Size", "Maximum Pool Size", "Connection Timeout",
                "Command Timeout", "CursorAsDataRead"
            ]
        },
        {
            "Kingbase",
            [
                "Pooling", "Minimum Pool Size", "Maximum Pool Size", "Connection Timeout",
                "Command Timeout", "CursorAsDataRead"
            ]
        },
        { "Vastbase", ["No Reset On Close"] },
        { "Doris", ["Charset", "Pooling"] },
        { "ClickHouse", [] },
        { "MongoDb", [] },
        { "Oscar", [] },
        { "DuckDB", [] },
        { "OceanBaseForOracle", [] },
        { "QuestDB", [] },
        { "TDengine", [] },
        { "GBase", [] }
    };

    public static TheoryData<string, string[]> OptionalCatalogs() => new()
    {
        {
            "MySql",
            [
                "SslMode", "AllowPublicKeyRetrieval", "Convert Zero Datetime",
                "Allow Zero Datetime", "TreatTinyAsBoolean"
            ]
        },
        {
            "PolarDB",
            [
                "SslMode", "AllowPublicKeyRetrieval", "Convert Zero Datetime",
                "Allow Zero Datetime", "TreatTinyAsBoolean"
            ]
        },
        { "Tidb", ["tidb_txn_mode", "tidb_isolation_read_engines"] },
        { "OceanBase", [] },
        { "PostgreSQL", ["SSL Mode", "Application Name", "Keepalive"] },
        {
            "GaussDB",
            ["SSL Mode", "Trust Server Certificate", "Encoding", "Search Path"]
        },
        {
            "OpenGauss",
            ["SSL Mode", "Trust Server Certificate", "Encoding", "Search Path"]
        },
        {
            "GaussDBNative",
            ["SSL Mode", "Trust Server Certificate", "Encoding", "Search Path"]
        },
        {
            "SqlServer",
            ["MultipleActiveResultSets", "Application Name", "Integrated Security"]
        },
        {
            "Oracle",
            ["Incr Pool Size", "Decr Pool Size", "Statement Cache Size"]
        },
        { "Sqlite", ["Password"] },
        { "QuestDB", ["ServerCompatibilityMode"] },
        { "TDengine", ["TsType"] },
        { "Dm", ["PORT", "HOST", "Encrypt"] },
        { "GBase", ["Protocol", "Db_locale", "Client_locale"] },
        {
            "Kdbndp",
            ["SSL Mode", "Trust Server Certificate", "Encoding"]
        },
        {
            "Kingbase",
            ["SSL Mode", "Trust Server Certificate", "Encoding"]
        },
        { "GoldenDB", [] },
        { "HG", [] },
        { "Vastbase", [] },
        { "ClickHouse", [] },
        { "MongoDb", [] },
        { "Oscar", [] },
        { "DuckDB", [] },
        { "OceanBaseForOracle", [] }
    };

    public static TheoryData<string, string[]> OptimizationCatalogs() => new()
    {
        {
            "MySql",
            ["enableBulkCopy", "disableNvarchar", "maxPoolSize", "charset", "enableSsl", "allowUserVariables"]
        },
        {
            "PolarDB",
            ["enableBulkCopy", "disableNvarchar", "maxPoolSize", "charset", "enableSsl", "allowUserVariables"]
        },
        {
            "Tidb",
            ["enableHints", "pessimisticTxn", "maxPoolSize", "enableBulkCopy", "disableNvarchar"]
        },
        {
            "OceanBase",
            ["disablePooling", "enableHints", "maxPoolSize", "tenantMode", "enableBulkCopy", "disableNvarchar"]
        },
        {
            "OceanBaseForOracle",
            ["camelCase", "enableIdentity", "maxParamLength", "disableNvarchar"]
        },
        {
            "PostgreSQL",
            ["autoToLower", "enableILike", "identityStrategy", "autoToLowerCodeFirst"]
        },
        {
            "GaussDB",
            ["nativeDriver", "isOpenGauss", "schema", "typeMapping", "batchSize", "maxPoolSize"]
        },
        {
            "GaussDBNative",
            ["nativeDriver", "isOpenGauss", "schema", "typeMapping", "batchSize", "maxPoolSize"]
        },
        {
            "OpenGauss",
            ["nativeDriver", "isOpenGauss", "schema", "typeMapping", "batchSize", "maxPoolSize"]
        },
        {
            "Dm",
            ["lowercaseTables", "dockerMysqlMode", "schema", "clobOptimization", "maxPoolSize"]
        },
        { "GoldenDB", ["disablePooling", "maxPoolSize"] },
        { "HG", ["autoToLower", "disablePooling", "maxPoolSize"] },
        { "Oscar", ["maxPoolSize"] },
        {
            "Kdbndp",
            [
                "mode", "camelCase", "enableCursor", "enableJson", "enableGeometry", "enableArray",
                "schema", "maxPoolSize", "disableNvarchar"
            ]
        },
        {
            "Kingbase",
            [
                "mode", "camelCase", "enableCursor", "enableJson", "enableGeometry", "enableArray",
                "schema", "maxPoolSize", "disableNvarchar"
            ]
        },
        {
            "Oracle",
            ["camelCase", "enableIdentity", "maxParamLength", "disableNvarchar"]
        },
        { "SqlServer", ["disableNvarchar", "enableNoLock", "disableNoLockWithTran"] },
        { "MongoDb", ["maxPoolSize"] },
        { "Sqlite", ["enableDefaultValue", "enableDescription", "enableDropColumn"] },
        { "ClickHouse", ["maxPoolSize"] },
        { "Vastbase", ["autoToLower", "noResetOnClose", "maxPoolSize", "disableNvarchar"] },
        { "DuckDB", [] },
        { "GBase", [] },
        { "QuestDB", [] },
        { "TDengine", [] }
    };
}
