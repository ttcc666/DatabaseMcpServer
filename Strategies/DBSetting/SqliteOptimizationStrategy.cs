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

    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        // SQLite CodeFirst 默认值支持（需要 SqlSugarCore 5.1.4.108-preview23+）
        if (optimizationSettings != null &&
            optimizationSettings.TryGetValue("enableDefaultValue", out var enableDefaultValueStr) &&
            bool.TryParse(enableDefaultValueStr, out var enableDefaultValue))
        {
            settings.SqliteCodeFirstEnableDefaultValue = enableDefaultValue;
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
            _logger?.LogDebug("SQLite 启用 CodeFirst 备注: {Enabled}", enableDescription);
        }
        else
        {
            // 默认启用
            settings.SqliteCodeFirstEnableDescription = true;
        }

        if (optimizationSettings == null) return;

        // SQLite CodeFirst 删除列支持（需要 SqlSugarCore 5.1.4.118-preview04+，仅支持 .NET Core）
        if (optimizationSettings.TryGetValue("enableDropColumn", out var enableDropColumnStr) &&
            bool.TryParse(enableDropColumnStr, out var enableDropColumn))
        {
            settings.SqliteCodeFirstEnableDropColumn = enableDropColumn;
            _logger?.LogDebug("SQLite 启用 CodeFirst 删除列: {Enabled}", enableDropColumn);
        }

        _logger?.LogDebug("应用 SQLite 性能优化配置");
    }

    public string GetDescription()
    {
        return "SQLite 性能优化：共享缓存 + CodeFirst 增强 + 内存模式支持";
    }
}