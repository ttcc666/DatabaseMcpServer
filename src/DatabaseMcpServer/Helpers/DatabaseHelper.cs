using DatabaseMcpServer.Interfaces;
using Microsoft.Extensions.Logging;
using SqlSugar;
using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using DbType = SqlSugar.DbType;

namespace DatabaseMcpServer.Helpers;

/// <summary>
/// 数据库操作辅助类。
/// </summary>
internal class DatabaseHelper : IDatabaseHelperService
{
    private static readonly string[] DangerousSqlPatternStrings =
    [
        @"\bDROP\s+TABLE\b",
        @"\bDROP\s+DATABASE\b",
        @"\bTRUNCATE\s+TABLE\b",
        @"\bALTER\s+TABLE\b",
        @"\bCREATE\s+TABLE\b"
    ];

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
    public DbType ParseDbType(string dbType)
    {
        return dbType.ToLowerInvariant() switch
        {
            "mysql" => DbType.MySql,
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
            "polardb" => DbType.PolarDB,
            "clickhouse" => DbType.ClickHouse,
            "opengauss" => DbType.OpenGauss,
            "gaussdb" => DbType.GaussDB,
            "gaussdbnative" => DbType.GaussDBNative,
            _ => throw new ArgumentException($"不支持的数据库类型: {dbType}。支持的数据库类型包括：mysql, postgresql, sqlserver, oracle, mongodb, sqlite, clickhouse, tidb, oceanbase, oceanbasefororacle, questdb, tdengine, duckdb, doris, dm, kdbndp, kingbase, oscar, hg, vastbase, goldendb, gbase, polardb, gaussdb, opengauss, gaussdbnative")
        };
    }

    /// <summary>
    /// 解析 JSON 格式的参数字符串为 SqlSugar 参数数组。
    /// </summary>
    public SugarParameter[]? ParseParameters(string? parametersJson)
    {
        if (string.IsNullOrWhiteSpace(parametersJson))
        {
            return null;
        }

        var paramsDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(parametersJson);
        if (paramsDict == null)
        {
            return null;
        }

        return paramsDict
            .Select(kvp => new SugarParameter(kvp.Key, JsonElementValueConverter.ConvertToValue(kvp.Value)))
            .ToArray();
    }

    /// <summary>
    /// 检测 SQL 语句中是否包含危险操作。
    /// </summary>
    public bool DetectDangerousOperation(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return false;
        }

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
            return [];
        }

        return
        [
            .. config.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(pattern => new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled))
        ];
    }

    private bool IsSqlWhitelisted(string sql)
    {
        if (_ddlWhitelistPatterns.Length == 0)
        {
            return false;
        }

        return _ddlWhitelistPatterns.Any(regex => regex.IsMatch(sql));
    }

    private static string TruncateForLog(string sql)
    {
        const int maxLength = 200;
        return sql.Length > maxLength ? $"{sql[..maxLength]}..." : sql;
    }
}
