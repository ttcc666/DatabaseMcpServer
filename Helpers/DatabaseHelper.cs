using DatabaseMcpServer.Interfaces;
using Microsoft.Extensions.Logging;
using SqlSugar;
using System.Data;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using DbType = SqlSugar.DbType;

namespace DatabaseMcpServer.Helpers;

/// <summary>
/// 数据库操作辅助类。
/// </summary>
internal class DatabaseHelper : IDatabaseHelperService
{
    private static readonly string[] DangerousSqlPatternStrings = new[]
    {
        @"\bDROP\s+TABLE\b",
        @"\bDROP\s+DATABASE\b",
        @"\bTRUNCATE\s+TABLE\b",
        @"\bALTER\s+TABLE\b",
        @"\bCREATE\s+TABLE\b"
    };

    private readonly ILogger<DatabaseHelper> _logger;
    private readonly Regex[] _dangerousSqlPatterns;
    private readonly Regex[] _ddlWhitelistPatterns;

    public DatabaseHelper(ILogger<DatabaseHelper> logger)
    {
        _logger = logger;
        _dangerousSqlPatterns = DangerousSqlPatternStrings
            .Select(pattern => new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled))
            .ToArray();
        _ddlWhitelistPatterns = LoadWhitelistPatterns();
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

        var paramsDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(parametersJson);
        if (paramsDict == null)
            return null;

        return paramsDict.Select(kvp => new SugarParameter(kvp.Key, ConvertJsonElementToValue(kvp.Value))).ToArray();
    }

    /// <summary>
    /// 将 JsonElement 转换为实际的值类型。
    /// </summary>
    private static object? ConvertJsonElementToValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var longValue) ? longValue : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElementToValue).ToArray(),
            JsonValueKind.Object => JsonSerializer.Deserialize<Dictionary<string, object>>(element.GetRawText()),
            _ => element.GetRawText()
        };
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

        var config = new ConnectionConfig
        {
            ConnectionString = connectionString,
            DbType = dbType,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute,
            // 性能优化配置
            MoreSettings = CreateOptimizedSettings(dbType)
        };

        var client = new SqlSugarClient(config);

        // 配置 SQL 执行日志
        client.Aop.OnLogExecuting = (sql, pars) =>
        {
            var parameters = pars?.Length > 0 ?
                string.Join(", ", pars.Select(p => $"{p.ParameterName}={p.Value}")) :
                "无参数";
            _logger.LogInformation("执行SQL: {Sql} | 参数: {Parameters}", sql, parameters);
        };

        // 配置错误日志
        client.Aop.OnError = (exp) =>
        {
            _logger.LogError(exp.Sql, "SQL执行错误: {Message}", exp.Message);
        };

        return client;
    }

    /// <summary>
    /// 根据数据库类型创建优化的配置设置
    /// 使用策略模式，方便扩展新的数据库类型
    /// </summary>
    private ConnMoreSettings CreateOptimizedSettings(DbType dbType)
    {
        var settings = new ConnMoreSettings
        {
            // 通用性能优化
            IsAutoRemoveDataCache = true, // 自动移除数据缓存，避免内存泄漏
            SqlServerCodeFirstNvarchar = true // SQL Server CodeFirst 使用 nvarchar
        };

        // 使用策略工厂获取对应数据库的优化策略
        var strategyFactory = new Strategies.DatabaseOptimizationStrategyFactory(_logger);
        var strategy = strategyFactory.GetStrategy(dbType);

        // 应用数据库特定的优化配置（DatabaseHelper 不使用 JSON 配置，传递 null）
        strategy.ApplyOptimizations(settings, null);

        return settings;
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
    /// 可通过设置环境变量 <c>DB_DDL_WHITELIST</c> (分号分隔的正则表达式) 来放行特定 DDL。
    /// </remarks>
    public bool DetectDangerousOperation(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return false;

        if (IsSqlWhitelisted(sql))
        {
            _logger.LogDebug("SQL 命中白名单，跳过危险检测: {SqlSample}", TruncateForLog(sql));
            return false;
        }

        foreach (var regex in _dangerousSqlPatterns)
        {
            if (regex.IsMatch(sql))
            {
                _logger.LogWarning("检测到危险 SQL: Pattern={Pattern} | SqlSample={SqlSample}", regex.ToString(), TruncateForLog(sql));
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 将 DataTable 转换为字典集合，方便统一序列化输出。
    /// </summary>
    public List<Dictionary<string, object?>> ConvertDataTableToList(DataTable dataTable)
    {
        var rows = new List<Dictionary<string, object?>>();
        foreach (DataRow row in dataTable.Rows)
        {
            var dict = new Dictionary<string, object?>();
            foreach (DataColumn col in dataTable.Columns)
            {
                dict[col.ColumnName] = row[col] == DBNull.Value ? null : row[col];
            }
            rows.Add(dict);
        }

        return rows;
    }

    private static Regex[] LoadWhitelistPatterns()
    {
        var config = Environment.GetEnvironmentVariable("DB_DDL_WHITELIST");
        if (string.IsNullOrWhiteSpace(config))
        {
            return Array.Empty<Regex>();
        }

        return config
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(pattern => new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled))
            .ToArray();
    }

    private bool IsSqlWhitelisted(string sql)
    {
        if (_ddlWhitelistPatterns.Length == 0)
            return false;

        return _ddlWhitelistPatterns.Any(regex => regex.IsMatch(sql));
    }

    private static string TruncateForLog(string sql)
    {
        const int maxLength = 200;
        return sql.Length > maxLength ? $"{sql[..maxLength]}..." : sql;
    }
}