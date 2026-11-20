using DatabaseMcpServer.Models;

namespace DatabaseMcpServer.Helpers;

internal static class DatabaseConfigurationValidator
{
    public static ValidationReport Validate(DatabasesConfig? config, bool requireConnections = false)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var databases = config?.Databases;
        if (databases == null)
        {
            return new(0, ["配置缺少有效的 databases 列表。"]);
        }

        if (requireConnections && databases.Count == 0)
        {
            errors.Add("配置文件中未找到有效的数据库配置。");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var defaultCount = 0;
        for (var i = 0; i < databases.Count; i++)
        {
            var item = databases[i];
            if (item == null)
            {
                errors.Add($"第 {i + 1} 个数据库连接不能为空。");
                continue;
            }

            if (item.IsDefault) defaultCount++;
            try { _ = DatabaseAccessPolicy.Parse(item.AccessMode); }
            catch (ArgumentException ex) { errors.Add($"数据库连接 '{item.Name}': {ex.Message}"); }
            if (string.IsNullOrWhiteSpace(item.Name))
                errors.Add($"第 {i + 1} 个数据库连接缺少 name。");
            else if (!names.Add(item.Name))
                errors.Add($"存在重复的数据库连接名称: {item.Name}");

            if (string.IsNullOrWhiteSpace(item.ConnectionString))
                errors.Add($"数据库连接 '{item.Name}' 缺少 connectionString。");
            if (string.IsNullOrWhiteSpace(item.DbType))
                errors.Add($"数据库连接 '{item.Name}' 缺少 dbType。");
            else
            {
                try
                {
                    var product = DatabaseHelper.ParseSupportedDbType(item.DbType);
                    if (DatabaseProviderCatalog.UnavailableReason(product) is { } unavailable)
                        warnings.Add($"数据库连接 '{item.Name}': {unavailable}");
                    if (!string.IsNullOrWhiteSpace(item.ConnectionString) &&
                        DatabaseProviderCatalog.ConnectionStringError(product, item.ConnectionString) is { } invalid)
                        errors.Add($"数据库连接 '{item.Name}': {invalid}");
                    errors.AddRange(DatabaseOptimizationOptions.Errors(product, item.OptimizationSettings)
                        .Select(error => $"数据库连接 '{item.Name}': {error}"));
                }
                catch (ArgumentException) { errors.Add($"数据库连接 '{item.Name}' 的 dbType 不受支持。"); }
            }
        }

        if (defaultCount > 1)
            errors.Add($"默认数据库连接数量超过 1 个，当前为 {defaultCount} 个。");
        return new(defaultCount, errors) { Warnings = warnings };
    }

    public static void ThrowIfInvalid(DatabasesConfig config, bool requireConnections = false)
    {
        var report = Validate(config, requireConnections);
        if (report.Errors.Count > 0)
            throw new InvalidOperationException(string.Join("\n", report.Errors));
    }

    internal sealed record ValidationReport(int DefaultDatabaseCount, List<string> Errors)
    {
        public List<string> Warnings { get; init; } = [];
    }
}
