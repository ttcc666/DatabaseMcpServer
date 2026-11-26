using System.Globalization;
using SqlSugar;

namespace DatabaseMcpServer.Helpers;

internal static class DatabaseOptimizationOptions
{
    private static readonly HashSet<string> Advisory = new(StringComparer.Ordinal)
    {
        "maxPoolSize", "disablePooling", "enableSsl", "charset", "allowUserVariables", "schema", "noResetOnClose", "nativeDriver", "tenantMode"
    };
    private static readonly HashSet<string> NotImplemented = new(StringComparer.Ordinal)
    {
        "enableBulkCopy", "enableCursor", "enableJson", "enableGeometry", "enableArray", "clobOptimization",
        "typeMapping", "batchSize", "enableHints", "pessimisticTxn"
    };

    public static Dictionary<string, object?> Mapped(DbType product, ConnMoreSettings s) => product switch
    {
        DbType.MySql or DbType.Tidb or DbType.TDSQL or DbType.PolarDB or DbType.OceanBase or DbType.Doris or DbType.GoldenDB
            => new() { ["disableNvarchar"] = s.DisableNvarchar },
        DbType.PostgreSQL => new() { ["autoToLower"] = s.PgSqlIsAutoToLower, ["autoToLowerCodeFirst"] = s.PgSqlIsAutoToLowerCodeFirst,
            ["enableILike"] = s.EnableILike, ["identityStrategy"] = s.PostgresIdentityStrategy.ToString() },
        DbType.SqlServer => new() { ["enableNoLock"] = s.IsWithNoLockQuery, ["disableNoLockWithTran"] = s.DisableWithNoLockWithTran, ["disableNvarchar"] = s.DisableNvarchar },
        DbType.Sqlite => new() { ["enableDefaultValue"] = s.SqliteCodeFirstEnableDefaultValue,
            ["enableDescription"] = s.SqliteCodeFirstEnableDescription, ["enableDropColumn"] = s.SqliteCodeFirstEnableDropColumn },
        DbType.Oracle or DbType.OceanBaseForOracle => new() { ["camelCase"] = !s.IsAutoToUpper, ["enableIdentity"] = s.EnableOracleIdentity,
            ["maxParamLength"] = s.MaxParameterNameLength, ["disableNvarchar"] = s.DisableNvarchar },
        DbType.Dm => new() { ["lowercaseTables"] = !s.IsAutoToUpper, ["dockerMysqlMode"] = s.DatabaseModel == DbType.MySql },
        DbType.Kdbndp => new() { ["mode"] = s.DatabaseModel.ToString(), ["camelCase"] = !s.IsAutoToUpper, ["disableNvarchar"] = s.DisableNvarchar },
        DbType.HG or DbType.TDengine => new() { ["autoToLower"] = s.PgSqlIsAutoToLower, ["autoToLowerCodeFirst"] = s.PgSqlIsAutoToLowerCodeFirst },
        DbType.Vastbase or DbType.GaussDB or DbType.OpenGauss
            => new() { ["autoToLower"] = s.PgSqlIsAutoToLower, ["autoToLowerCodeFirst"] = s.PgSqlIsAutoToLowerCodeFirst, ["disableNvarchar"] = s.DisableNvarchar },
        DbType.GaussDBNative => new() { ["databaseModel"] = s.DatabaseModel.ToString(), ["isOpenGauss"] = s.DatabaseModel == DbType.OpenGauss },
        _ => new()
    };

    public static bool IsValid(string key, string value) => key switch
    {
        "maxPoolSize" or "batchSize" or "maxParamLength" => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0,
        "identityStrategy" => IsOneOf(value, "Serial", "Identity"),
        "mode" => IsOneOf(value, "Oracle", "MySql", "PostgreSQL", "SqlServer"),
        "databaseModel" => IsOneOf(value, "GaussDB", "OpenGauss"),
        "schema" or "charset" or "tenantMode" => !string.IsNullOrWhiteSpace(value),
        _ => bool.TryParse(value, out _)
    };

    private static bool IsOneOf(string value, params string[] choices) => choices.Contains(value, StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<string> Errors(DbType product, IReadOnlyDictionary<string, string>? options)
    {
        if (options == null) yield break;
        var mapped = Mapped(product, new());
        foreach (var pair in options)
            if ((mapped.ContainsKey(pair.Key) || Advisory.Contains(pair.Key) || NotImplemented.Contains(pair.Key)) && !IsValid(pair.Key, pair.Value))
                yield return $"optimizationSettings.{pair.Key} 的值无效。";
        if (product == DbType.GaussDBNative && options.TryGetValue("databaseModel", out var model) &&
            options.TryGetValue("isOpenGauss", out var legacy) && bool.TryParse(legacy, out var open) &&
            IsValid("databaseModel", model) && open != model.Equals("OpenGauss", StringComparison.OrdinalIgnoreCase))
            yield return "databaseModel 与 isOpenGauss 配置冲突。";
    }

    public static Dictionary<string, string>? AppliedOptions(DbType product, IReadOnlyDictionary<string, string>? options)
    {
        if (options == null) return null;
        var mapped = Mapped(product, new());
        return options.Where(pair => mapped.ContainsKey(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    public static object[] Describe(DbType product, IReadOnlyDictionary<string, string>? options, ConnMoreSettings settings)
    {
        var mapped = Mapped(product, settings);
        return (options ?? new Dictionary<string, string>()).Select(pair =>
        {
            var applied = mapped.TryGetValue(pair.Key, out var effective);
            var known = applied || Advisory.Contains(pair.Key) || NotImplemented.Contains(pair.Key);
            var status = !known ? "unknown" : !IsValid(pair.Key, pair.Value) ? "ignored-invalid" :
                applied ? "mapped" : Advisory.Contains(pair.Key) ? "advisory" : "not-applicable";
            return (object)new { name = pair.Key, status,
                configured = known && IsValid(pair.Key, pair.Value) && pair.Key is not ("schema" or "charset" or "tenantMode") ? SafeValue(pair.Value) : "[redacted]",
                effective = applied ? effective : null,
                source = applied ? "SqlSugar.ConnMoreSettings" : "not-applied",
                scope = applied ? product == DbType.Sqlite ? "CodeFirst-only; schema tools use DbMaintenance" :
                    "ORM-generated-SQL; does not rewrite raw SQL" : "not-applied-by-MCP",
                note = pair.Key == "nativeDriver" ? "驱动由 dbType 决定；此旧键保留但不切换驱动。" :
                    Advisory.Contains(pair.Key) ? "请使用原生连接字符串配置；optimizationSettings 不覆盖驱动选项。" :
                    NotImplemented.Contains(pair.Key) ? "当前 MCP 未实现此开关对应的执行功能。" : null };
        }).ToArray();
    }

    private static string SafeValue(string value)
        => value.Length <= 64 && value.All(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.') ? value : "[redacted]";
}
