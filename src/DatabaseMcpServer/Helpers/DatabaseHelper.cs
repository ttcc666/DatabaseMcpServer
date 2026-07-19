using DatabaseMcpServer.Interfaces;
using Microsoft.Extensions.Logging;
using SqlSugar;
using System.Data;
using System.Text;
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

    private static readonly Regex GoBatchSeparatorRegex = new(
        @"(?im)^[\t ]*GO(?:[\t ]+\d+)?[\t ]*(?:\r?\n|$)",
        RegexOptions.Compiled);

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

        if (ContainsUnboundedMutation(sql))
        {
            _logger.LogWarning("检测到无 WHERE 的 UPDATE/DELETE: {SqlSample}", TruncateForLog(sql));
            return true;
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

    private static bool ContainsUnboundedMutation(string sql)
    {
        var sanitized = GoBatchSeparatorRegex.Replace(
            StripSqlCommentsAndLiterals(sql),
            ";");

        foreach (var statement in sanitized.Split(';'))
        {
            if (StatementContainsUnboundedMutation(statement))
            {
                return true;
            }
        }

        return false;
    }

    private static bool StatementContainsUnboundedMutation(string statement)
    {
        var scopes = new Stack<MutationScope>();
        scopes.Push(new MutationScope());

        for (var index = 0; index < statement.Length;)
        {
            var current = statement[index];
            if (current == '(')
            {
                scopes.Push(new MutationScope());
                index++;
                continue;
            }

            if (current == ')')
            {
                if (scopes.Count > 1 && IsUnbounded(scopes.Pop()))
                {
                    return true;
                }

                index++;
                continue;
            }

            if (!IsSqlTokenStart(current))
            {
                index++;
                continue;
            }

            var start = index++;
            while (index < statement.Length && IsSqlTokenPart(statement[index]))
            {
                index++;
            }

            var token = statement[start..index];
            var scope = scopes.Peek();
            scope.FirstToken ??= token;

            if (!scope.MutationFound &&
                (string.Equals(token, "UPDATE", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(token, "DELETE", StringComparison.OrdinalIgnoreCase)) &&
                (string.Equals(scope.FirstToken, token, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(scope.FirstToken, "WITH", StringComparison.OrdinalIgnoreCase)))
            {
                scope.MutationFound = true;
            }
            else if (scope.MutationFound &&
                     string.Equals(token, "WHERE", StringComparison.OrdinalIgnoreCase))
            {
                scope.WhereFound = true;
            }
        }

        return scopes.Any(IsUnbounded);
    }

    private static bool IsUnbounded(MutationScope scope)
        => scope.MutationFound && !scope.WhereFound;

    private static bool IsSqlTokenStart(char value)
        => char.IsLetter(value) || value is '_' or '$';

    private static bool IsSqlTokenPart(char value)
        => char.IsLetterOrDigit(value) || value is '_' or '$';

    private static string StripSqlCommentsAndLiterals(string sql)
    {
        var builder = new StringBuilder(sql.Length);

        for (var index = 0; index < sql.Length;)
        {
            var current = sql[index];

            if (current == '-' && index + 1 < sql.Length && sql[index + 1] == '-')
            {
                index += 2;
                while (index < sql.Length && sql[index] != '\n')
                {
                    index++;
                }

                if (index < sql.Length)
                {
                    builder.Append('\n');
                    index++;
                }

                continue;
            }

            if (current == '/' && index + 1 < sql.Length && sql[index + 1] == '*')
            {
                index += 2;
                while (index + 1 < sql.Length && !(sql[index] == '*' && sql[index + 1] == '/'))
                {
                    builder.Append(sql[index] == '\n' ? '\n' : ' ');
                    index++;
                }

                index = Math.Min(index + 2, sql.Length);
                continue;
            }

            if (current is '\'' or '"' or '`' or '[')
            {
                var closing = current == '[' ? ']' : current;
                builder.Append(' ');
                index++;

                while (index < sql.Length)
                {
                    if (current != '[' &&
                        sql[index] == '\\' &&
                        index + 1 < sql.Length)
                    {
                        builder.Append("  ");
                        index += 2;
                        continue;
                    }

                    if (sql[index] == closing)
                    {
                        if (index + 1 < sql.Length && sql[index + 1] == closing)
                        {
                            builder.Append("  ");
                            index += 2;
                            continue;
                        }

                        builder.Append(' ');
                        index++;
                        break;
                    }

                    builder.Append(sql[index] == '\n' ? '\n' : ' ');
                    index++;
                }

                continue;
            }

            if (current == '$' && TryReadDollarQuoteDelimiter(sql, index, out var delimiter))
            {
                var closingIndex = sql.IndexOf(delimiter, index + delimiter.Length, StringComparison.Ordinal);
                var end = closingIndex < 0 ? sql.Length : closingIndex + delimiter.Length;
                while (index < end)
                {
                    builder.Append(sql[index] == '\n' ? '\n' : ' ');
                    index++;
                }

                continue;
            }

            builder.Append(current);
            index++;
        }

        return builder.ToString();
    }

    private static bool TryReadDollarQuoteDelimiter(string sql, int start, out string delimiter)
    {
        var end = sql.IndexOf('$', start + 1);
        if (end < 0)
        {
            delimiter = string.Empty;
            return false;
        }

        var tag = sql.AsSpan(start + 1, end - start - 1);
        if (!tag.IsEmpty && !(char.IsLetter(tag[0]) || tag[0] == '_'))
        {
            delimiter = string.Empty;
            return false;
        }

        for (var index = 1; index < tag.Length; index++)
        {
            if (!(char.IsLetterOrDigit(tag[index]) || tag[index] == '_'))
            {
                delimiter = string.Empty;
                return false;
            }
        }

        delimiter = sql[start..(end + 1)];
        return true;
    }

    private sealed class MutationScope
    {
        public string? FirstToken { get; set; }

        public bool MutationFound { get; set; }

        public bool WhereFound { get; set; }
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
