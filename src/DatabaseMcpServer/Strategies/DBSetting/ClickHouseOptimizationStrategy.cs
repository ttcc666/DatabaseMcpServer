using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies.DBSetting;

/// <summary>
/// ClickHouse 性能优化策略（轻量提示）
/// </summary>
public class ClickHouseOptimizationStrategy : IDatabaseOptimizationStrategy
{
    private readonly ILogger? _logger;

    public ClickHouseOptimizationStrategy(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 应用 ClickHouse 优化配置（连接池等提示，默认保留 nvarchar）。
    /// </summary>
    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        // 默认保留 nvarchar
        settings.DisableNvarchar = false;

        if (optimizationSettings == null)
        {
            _logger?.LogDebug("应用 ClickHouse 默认性能优化配置");
            return;
        }

        var applied = new List<string>();

        if (optimizationSettings.TryGetValue("maxPoolSize", out var maxPoolSizeStr) &&
            int.TryParse(maxPoolSizeStr, out var maxPoolSize))
        {
            _logger?.LogDebug("ClickHouse 最大连接池: {MaxPoolSize}", maxPoolSize);
            applied.Add($"maxPoolSize={maxPoolSize}");
        }

        if (applied.Count > 0)
        {
            _logger?.LogDebug("ClickHouse 优化配置选项: {Options}", string.Join(", ", applied));
        }

        _logger?.LogDebug("应用 ClickHouse 性能优化配置");
    }

    /// <summary>
    /// 获取 ClickHouse 优化策略描述。
    /// </summary>
    public string GetDescription()
    {
        return "ClickHouse 优化：连接池提示";
    }
}