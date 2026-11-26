using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies.DBSetting;

/// <summary>
/// GoldenDB（MySQL 兼容）性能优化策略
/// </summary>
public class GoldenDbOptimizationStrategy : IDatabaseOptimizationStrategy
{
    private readonly ILogger? _logger;

    public GoldenDbOptimizationStrategy(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 应用 GoldenDB 优化配置（MySQL 兼容，连接池建议等）。
    /// </summary>
    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        // GoldenDB 兼容 MySQL，默认不禁用 nvarchar，按需显式关闭；连接池建议在连接串设置 Pooling=false
        settings.DisableNvarchar = false;

        if (optimizationSettings == null)
        {
            _logger?.LogDebug("应用 GoldenDB 默认性能优化配置");
            return;
        }

        if (optimizationSettings.TryGetValue("disableNvarchar", out var disableNvarcharStr) &&
            bool.TryParse(disableNvarcharStr, out var disableNvarchar))
        {
            settings.DisableNvarchar = disableNvarchar;
            _logger?.LogDebug("GoldenDB 禁用 Nvarchar: {Disabled}", disableNvarchar);
        }

        _logger?.LogDebug("应用 GoldenDB 性能优化配置");
    }

    /// <summary>
    /// 获取 GoldenDB 优化策略描述。
    /// </summary>
    public string GetDescription()
    {
        return "GoldenDB 优化：MySQL 兼容 + 连接池提示";
    }
}
