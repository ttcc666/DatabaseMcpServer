using System;
using System.Collections.Generic;
using System.Linq;

namespace DatabaseMcpServer.Gui.Core.Services;

/// <summary>
/// DMS 支持的数据库类型目录。配置文件中统一使用这里的标准大小写形式。
/// </summary>
public static class DatabaseTypeCatalog
{
    public static IReadOnlyList<string> All { get; } =
    [
        "MySql",
        "PostgreSQL",
        "SqlServer",
        "Sqlite",
        "Oracle",
        "MongoDb",
        "QuestDB",
        "TDengine",
        "DuckDB",
        "Doris",
        "Dm",
        "Kdbndp",
        "Kingbase",
        "Oscar",
        "HG",
        "Vastbase",
        "GoldenDB",
        "GBase",
        "OceanBase",
        "OceanBaseForOracle",
        "Tidb",
        "PolarDB",
        "ClickHouse",
        "OpenGauss",
        "GaussDB",
        "GaussDBNative"
    ];

    private static readonly IReadOnlyDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["postgres"] = "PostgreSQL",
            ["postgresql"] = "PostgreSQL",
            ["sql server"] = "SqlServer",
            ["sqlserver"] = "SqlServer",
            ["mongodb"] = "MongoDb",
            ["mongo"] = "MongoDb",
            ["sqlite3"] = "Sqlite",
            ["questdb"] = "QuestDB",
            ["tdengine"] = "TDengine",
            ["duckdb"] = "DuckDB",
            ["gaussdbnative"] = "GaussDBNative",
            ["oceanbasefororacle"] = "OceanBaseForOracle"
        };

    /// <summary>
    /// 将大小写不同或常见别名转换为配置文件使用的标准值。
    /// 未知非空值只做 Trim，避免编辑器悄悄丢失未来版本新增的类型。
    /// </summary>
    public static string Normalize(string? dbType)
    {
        var value = dbType?.Trim() ?? string.Empty;
        if (value.Length == 0) return string.Empty;
        if (Aliases.TryGetValue(value, out var alias)) return alias;
        return All.FirstOrDefault(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase)) ?? value;
    }

    /// <summary>
    /// 尝试从旧配置中缺失的 dbType 推断数据库类型。只在特征明确时返回结果。
    /// </summary>
    public static string InferFromConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return string.Empty;
        var text = connectionString.Trim();
        if (text.StartsWith("mongodb://", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("mongodb+srv://", StringComparison.OrdinalIgnoreCase))
        {
            return "MongoDb";
        }

        var values = ParseKeyValues(text);
        var dataSource = Get(values, "data source");
        var dataSourceCompact = Get(values, "datasource");
        var port = Get(values, "port");
        if (string.IsNullOrWhiteSpace(port))
        {
            var server = Get(values, "server") ?? Get(values, "host");
            var separator = server?.LastIndexOf(':') ?? -1;
            if (separator >= 0 && separator < server!.Length - 1)
            {
                port = server[(separator + 1)..];
            }
        }

        if (!string.IsNullOrWhiteSpace(dataSourceCompact) &&
            IsEmbeddedDatabasePath(dataSourceCompact))
        {
            return "DuckDB";
        }

        if (!string.IsNullOrWhiteSpace(dataSource) && IsEmbeddedDatabasePath(dataSource))
        {
            return "Sqlite";
        }

        return port switch
        {
            "3306" => "MySql",
            "4000" => "Tidb",
            "5236" => "Dm",
            "5432" => "PostgreSQL",
            "54321" => "Kdbndp",
            "5866" => "HG",
            "6030" => "TDengine",
            "8123" => "ClickHouse",
            "8812" => "QuestDB",
            "9030" => "Doris",
            _ => InferFromKeys(values, dataSource)
        };
    }

    private static string InferFromKeys(IReadOnlyDictionary<string, string> values, string? dataSource)
    {
        if (!string.IsNullOrWhiteSpace(dataSource)) return "Oracle";
        if (ContainsKey(values, "encrypt") || ContainsKey(values, "trustservercertificate")) return "SqlServer";
        if (ContainsKey(values, "uid") && ContainsKey(values, "pwd")) return "MySql";
        return string.Empty;
    }

    private static bool IsEmbeddedDatabasePath(string value)
    {
        var path = value.Trim().Trim('"', '\'');
        return path.EndsWith(".db", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".sqlite3", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".duckdb", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("./", StringComparison.Ordinal) ||
               path.StartsWith(".\\", StringComparison.Ordinal) ||
               path.StartsWith("/", StringComparison.Ordinal) ||
               path.StartsWith("~/", StringComparison.Ordinal) ||
               (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' &&
                (path[2] == '\\' || path[2] == '/'));
    }

    private static Dictionary<string, string> ParseKeyValues(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0) continue;
            result[part[..separator].Trim()] = part[(separator + 1)..].Trim();
        }
        return result;
    }

    private static string? Get(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) ? value : null;

    private static bool ContainsKey(IReadOnlyDictionary<string, string> values, string key) =>
        values.Keys.Any(item => string.Equals(item, key, StringComparison.OrdinalIgnoreCase));
}
