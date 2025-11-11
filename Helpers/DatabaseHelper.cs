using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Encodings.Web;
using SqlSugar;
using DatabaseMcpServer.Interfaces;
using Microsoft.Extensions.Logging;

namespace DatabaseMcpServer.Helpers;

/// <summary>
/// 数据库操作辅助类。
/// </summary>
internal class DatabaseHelper : IDatabaseHelperService
{
    private readonly ILogger<DatabaseHelper> _logger;

    public DatabaseHelper(ILogger<DatabaseHelper> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 将字符串类型转换为 SqlSugar 的 DbType 枚举。
    /// </summary>
    /// <param name="dbType">数据库类型字符串</param>
    /// <returns>对应的 DbType 枚举值</returns>
    /// <exception cref="ArgumentException">当数据库类型不支持时抛出</exception>
    /// <remarks>
    /// 支持的数据库类型字符串包括：
    /// - 主流数据库: mysql, sqlserver, sqlite, postgresql, oracle
    /// - 国产数据库: dm (达梦), kdbndp/kingbase (人大金仓), oscar (神通), hg (瀚高), gbase (南大通用), xugu (虚谷), vastbase (海量), goldendb (GoldenDB)
    /// - 分布式数据库: oceanbase, tidb, polardb, doris, tdengine, questdb
    /// - 时序数据库: tdengine, questdb, clickhouse
    /// - 分析型数据库: duckdb, clickhouse, doris
    /// - 其他数据库: access, odbc, hana, db2, mongodb, custom
    /// - 特定版本: mysqlconnector, opengauss, gaussdb, gaussdbnative, oceanbasefororacle, tsql, tsqlforpgodbc
    /// </remarks>
    public DbType ParseDbType(string dbType)
    {
        return dbType.ToLowerInvariant() switch
        {
            // 主流数据库
            "mysql" => DbType.MySql,
            "sqlserver" => DbType.SqlServer,
            "sqlite" => DbType.Sqlite,
            "postgresql" => DbType.PostgreSQL,
            "oracle" => DbType.Oracle,

            // 国产数据库
            "dm" => DbType.Dm,
            "kdbndp" => DbType.Kdbndp,
            "kingbase" => DbType.Kdbndp,
            "oscar" => DbType.Oscar,
            "hg" => DbType.HG,
            "gbase" => DbType.GBase,
            "xugu" => DbType.Xugu,
            "vastbase" => DbType.Vastbase,
            "goldendb" => DbType.GoldenDB,

            // 分布式数据库
            "oceanbase" => DbType.OceanBase,
            "tidb" => DbType.Tidb,
            "polardb" => DbType.PolarDB,
            "doris" => DbType.Doris,

            // 时序数据库
            "tdengine" => DbType.TDengine,
            "questdb" => DbType.QuestDB,
            "clickhouse" => DbType.ClickHouse,

            // 分析型数据库
            "duckdb" => DbType.DuckDB,

            // 其他数据库
            "access" => DbType.Access,
            "odbc" => DbType.Odbc,
            "hana" => DbType.HANA,
            "db2" => DbType.DB2,
            "mongodb" => DbType.MongoDb,
            "custom" => DbType.Custom,

            // 特定版本和变体
            "mysqlconnector" => DbType.MySqlConnector,
            "opengauss" => DbType.OpenGauss,
            "gaussdb" => DbType.GaussDB,
            "gaussdbnative" => DbType.GaussDBNative,
            "oceanbasefororacle" => DbType.OceanBaseForOracle,
            "tdsql" => DbType.TDSQL,
            "tdsqlforpgodbc" => DbType.TDSQLForPGODBC,

            _ => throw new ArgumentException($"不支持的数据库类型: {dbType}。支持的数据库类型包括：mysql, sqlserver, sqlite, postgresql, oracle, dm, kdbndp, kingbase, oscar, hg, gbase, xugu, vastbase, goldendb, oceanbase, tidb, polardb, doris, tdengine, questdb, clickhouse, duckdb, access, odbc, hana, db2, mongodb, custom, mysqlconnector, opengauss, gaussdb, gaussdbnative, oceanbasefororacle, tsql, tsqlforpgodbc")
        };
    }

    /// <summary>
    /// 解析 JSON 格式的参数字符串为 SqlSugar 参数数组。
    /// </summary>
    /// <param name="parametersJson">JSON 格式的参数字符串</param>
    /// <returns>SqlSugar 参数数组,如果输入为空则返回 null</returns>
    public SugarParameter[]? ParseParameters(string? parametersJson)
    {
        if (string.IsNullOrWhiteSpace(parametersJson))
            return null;

        var paramsDict = JsonSerializer.Deserialize<Dictionary<string, object>>(parametersJson);
        if (paramsDict == null)
            return null;

        return paramsDict.Select(kvp => new SugarParameter(kvp.Key, kvp.Value)).ToArray();
    }

    /// <summary>
    /// 将对象序列化为格式化的 JSON 字符串。
    /// </summary>
    /// <param name="data">要序列化的数据对象</param>
    /// <returns>格式化的 JSON 字符串</returns>
    public string SerializeResult(object data)
    {
        return JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    /// <summary>
    /// 创建 SqlSugar 数据库客户端实例。
    /// </summary>
    /// <param name="connectionString">数据库连接字符串</param>
    /// <param name="dbType">数据库类型</param>
    /// <returns>配置好的 SqlSugarClient 实例</returns>
    public SqlSugarClient CreateClient(string connectionString, DbType dbType)
    {
        _logger.LogDebug("创建数据库客户端，类型: {DbType}", dbType);
        var client = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = connectionString,
            DbType = dbType,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute
        });

        // 配置 SQL 执行日志
        client.Aop.OnLogExecuting = (sql, pars) =>
        {
            var parameters = pars?.Length > 0 ?
                string.Join(", ", pars.Select(p => $"{p.ParameterName}={p.Value}")) :
                "无参数";
            _logger.LogInformation("执行SQL: {Sql} | 参数: {Parameters}", sql, parameters);
        };

        return client;
    }

    /// <summary>
    /// 检测 SQL 语句中是否包含危险操作。
    /// </summary>
    /// <param name="sql">要检测的 SQL 语句</param>
    /// <returns>如果包含危险操作返回 true,否则返回 false</returns>
    /// <remarks>
    /// 危险操作包括:
    /// - DROP TABLE (删除表)
    /// - DROP DATABASE (删除数据库)
    /// - TRUNCATE TABLE (截断表)
    /// - ALTER TABLE (修改表结构)
    /// - CREATE TABLE (创建表)
    /// </remarks>
    public bool DetectDangerousOperation(string sql)
    {
        var dangerousPatterns = new[]
        {
            @"\bDROP\s+TABLE\b",
            @"\bDROP\s+DATABASE\b",
            @"\bTRUNCATE\s+TABLE\b",
            @"\bALTER\s+TABLE\b",
            @"\bCREATE\s+TABLE\b"
        };

        return dangerousPatterns.Any(pattern =>
            Regex.IsMatch(sql, pattern, RegexOptions.IgnoreCase));
    }
}
