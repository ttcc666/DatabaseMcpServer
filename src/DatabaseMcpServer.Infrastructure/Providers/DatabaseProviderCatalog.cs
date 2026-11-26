using System.Reflection;
using DatabaseMcpServer.Models;
using SqlSugar;

namespace DatabaseMcpServer.Helpers;

internal static class DatabaseProviderCatalog
{
    public static DbType Parse(string dbType)
    {
        return dbType.ToLowerInvariant() switch
        {
            "mysql" => DbType.MySql,
            "mysqlconnector" => DbType.MySql,
            "postgresql" => DbType.PostgreSQL,
            "sqlserver" => DbType.SqlServer,
            "sqlite" => DbType.Sqlite,
            "oracle" => DbType.Oracle,
            "mongodb" => DbType.MongoDb,
            "questdb" => DbType.QuestDB,
            "tdengine" => DbType.TDengine,
            "duckdb" => DbType.DuckDB,
            "doris" => DbType.Doris,
            "dm" => DbType.Dm,
            "kdbndp" => DbType.Kdbndp,
            "kingbase" => DbType.Kdbndp,
            "oscar" => DbType.Oscar,
            "hg" => DbType.HG,
            "vastbase" => DbType.Vastbase,
            "goldendb" => DbType.GoldenDB,
            "gbase" => DbType.GBase,
            "oceanbase" => DbType.OceanBase,
            "oceanbasefororacle" => DbType.OceanBaseForOracle,
            "tidb" => DbType.Tidb,
            "tdsql" => DbType.TDSQL,
            "polardb" => DbType.PolarDB,
            "clickhouse" => DbType.ClickHouse,
            "opengauss" => DbType.OpenGauss,
            "gaussdb" => DbType.GaussDB,
            "gaussdbnative" => DbType.GaussDBNative,
            _ => throw new ArgumentException($"不支持的数据库类型: {dbType}。支持的数据库类型包括：mysql, mysqlconnector, postgresql, sqlserver, oracle, mongodb, sqlite, clickhouse, tidb, tdsql, oceanbase, oceanbasefororacle, questdb, tdengine, duckdb, doris, dm, kdbndp, kingbase, oscar, hg, vastbase, goldendb, gbase, polardb, gaussdb, opengauss, gaussdbnative")
        };
    }

    public static bool SupportsBoundedResults(DbType provider)
        => ProviderCapabilities.Supports(provider, DatabaseCapability.BoundedResults);

    public static bool SupportsEstimatedPlan(DbType provider)
        => ProviderCapabilities.Supports(provider, DatabaseCapability.EstimatedPlan);

    public static bool SupportsAtomicDml(DbType provider)
        => ProviderCapabilities.Supports(provider, DatabaseCapability.AtomicDml);

    internal static readonly IReadOnlyDictionary<DbType, string> ExtensionAssemblies = new Dictionary<DbType, string>
    {
        [DbType.ClickHouse] = "SqlSugar.ClickHouseCore",
        [DbType.MongoDb] = "SqlSugar.MongoDbCore",
        [DbType.GaussDBNative] = "SqlSugar.GaussDBCore",
        [DbType.OceanBaseForOracle] = "SqlSugar.OceanBaseForOracleCore",
        [DbType.HG] = "SqlSugar.HGCore",
        [DbType.GBase] = "SqlSugar.GBaseCore",
        [DbType.TDengine] = "SqlSugar.TDengineCore",
        [DbType.DuckDB] = "SqlSugar.DuckDBCore"
    };

    public static DbType DriverFamily(DbType product) => product switch
    {
        DbType.Tidb or DbType.OceanBase or DbType.PolarDB or DbType.GoldenDB or DbType.Doris or DbType.TDSQL => DbType.MySql,
        DbType.GaussDB or DbType.OpenGauss or DbType.Vastbase => DbType.PostgreSQL,
        _ => product
    };

    public static bool IsVerifiedProduct(DbType product)
        => product is DbType.Sqlite or DbType.MySql or DbType.PostgreSQL or DbType.SqlServer;

    public static string? UnavailableReason(DbType product)
    {
        if (!ExtensionAssemblies.TryGetValue(product, out var name)) return null;
        try { _ = Assembly.Load(name); return null; }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            return $"{product} 扩展程序集 {name}.dll 未安装或无法加载；当前发布不可用。";
        }
    }

    public static void EnsureAvailable(DbType product)
    {
        if (UnavailableReason(product) is { } reason)
            throw new DatabaseMcpException(DatabaseErrorCode.UnsupportedCapability, reason);
    }

    public static void EnsureSqlTools(DbType product)
    {
        if (product == DbType.MongoDb)
            throw new DatabaseMcpException(DatabaseErrorCode.UnsupportedCapability,
                "MongoDB 不支持通用 SQL 工具；请使用 mongo_find、mongo_aggregate、mongo_insert_many、mongo_update、mongo_delete 或 mongo_truncate。");
    }

    // Parse locally; never open connections, resolve SRV records, or return driver exception text.
    public static string? ConnectionStringError(DbType product, string connectionString)
    {
        try
        {
            _ = DriverFamily(product) switch
            {
                DbType.MySql => (object)new MySqlConnector.MySqlConnectionStringBuilder(connectionString),
                DbType.PostgreSQL or DbType.QuestDB => new Npgsql.NpgsqlConnectionStringBuilder(connectionString),
                DbType.SqlServer => new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString),
                DbType.Sqlite => new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connectionString),
                DbType.Kdbndp => new Kdbndp.KdbndpConnectionStringBuilder(connectionString),
                DbType.Oracle => new Oracle.ManagedDataAccess.Client.OracleConnectionStringBuilder(connectionString),
                DbType.Dm => new Dm.DmConnectionStringBuilder(connectionString),
                DbType.Oscar => new System.Data.OscarClient.OscarConnectionStringBuilder(connectionString),
                DbType.HG => new Nhgdb.NhgdbConnectionStringBuilder(connectionString),
                DbType.GaussDBNative => new OpenGauss.NET.OpenGaussConnectionStringBuilder(connectionString),
                DbType.MongoDb => MongoDocumentContract.ValidateUri(connectionString),
                _ => new System.Data.Common.DbConnectionStringBuilder { ConnectionString = connectionString }
            };
            return null;
        }
        catch (DatabaseMcpException ex) when (ex.ErrorCode == DatabaseErrorCode.InvalidParameters)
        {
            return ex.Message;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException or KeyNotFoundException)
        {
            return $"{product} 连接字符串选项无效，请检查字段名、值及驱动文档（连接细节已隐藏）。";
        }
    }
}
