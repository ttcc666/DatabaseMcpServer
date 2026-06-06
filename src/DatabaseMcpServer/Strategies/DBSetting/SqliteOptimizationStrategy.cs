using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies.DBSetting;

/// <summary>
/// SQLite 性能优化策略
/// 参考文档: Doc/donet5_sqlite.md
/// </summary>
public class SqliteOptimizationStrategy : IDatabaseOptimizationStrategy
{
    private readonly ILogger? _logger;

    public SqliteOptimizationStrategy(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 应用 SQLite 优化配置（默认启用 CodeFirst 默认值/备注等）。
    /// </summary>
    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        var appliedOptions = new List<string>();

        // SQLite CodeFirst 默认值支持（需要 SqlSugarCore 5.1.4.108-preview23+）
        if (optimizationSettings != null &&
            optimizationSettings.TryGetValue("enableDefaultValue", out var enableDefaultValueStr) &&
            bool.TryParse(enableDefaultValueStr, out var enableDefaultValue))
        {
            settings.SqliteCodeFirstEnableDefaultValue = enableDefaultValue;
            appliedOptions.Add($"enableDefaultValue={enableDefaultValue}");
            _logger?.LogDebug("SQLite 启用 CodeFirst 默认值: {Enabled}", enableDefaultValue);
        }
        else
        {
            // 默认启用
            settings.SqliteCodeFirstEnableDefaultValue = true;
        }

        // SQLite CodeFirst 备注支持（需要 SqlSugarCore 5.1.4.108-preview25+）
        if (optimizationSettings != null &&
            optimizationSettings.TryGetValue("enableDescription", out var enableDescriptionStr) &&
            bool.TryParse(enableDescriptionStr, out var enableDescription))
        {
            settings.SqliteCodeFirstEnableDescription = enableDescription;
            appliedOptions.Add($"enableDescription={enableDescription}");
            _logger?.LogDebug("SQLite 启用 CodeFirst 备注: {Enabled}", enableDescription);
        }
        else
        {
            // 默认启用
            settings.SqliteCodeFirstEnableDescription = true;
        }

        // 默认不启用删除列功能（仅 .NET Core 支持）
        settings.SqliteCodeFirstEnableDropColumn = false;

        if (optimizationSettings == null)
        {
            _logger?.LogDebug("应用 SQLite 默认性能优化配置（默认值/备注开启，删除列关闭）");
            return;
        }

        // SQLite CodeFirst 删除列支持（需要 SqlSugarCore 5.1.4.118-preview04+，仅支持 .NET Core）
        if (optimizationSettings.TryGetValue("enableDropColumn", out var enableDropColumnStr) &&
            bool.TryParse(enableDropColumnStr, out var enableDropColumn))
        {
            settings.SqliteCodeFirstEnableDropColumn = enableDropColumn;
            appliedOptions.Add($"enableDropColumn={enableDropColumn}");
            _logger?.LogDebug("SQLite 启用 CodeFirst 删除列: {Enabled}", enableDropColumn);
        }

        if (appliedOptions.Count > 0)
        {
            _logger?.LogDebug("SQLite 优化配置选项: {Options}", string.Join(", ", appliedOptions));
        }

        _logger?.LogDebug("应用 SQLite 性能优化配置");
    }

    /// <summary>
    /// 获取 SQLite 优化策略描述。
    /// </summary>
    public string GetDescription()
    {
        return "SQLite 性能优化：共享缓存 + CodeFirst 增强 + 内存模式支持";
    }
}
