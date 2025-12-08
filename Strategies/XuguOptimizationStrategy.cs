using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies;

/// <summary>
/// 虚谷数据库性能优化策略（轻量）
/// </summary>
public class XuguOptimizationStrategy : IDatabaseOptimizationStrategy
{
    private readonly ILogger? _logger;

    public XuguOptimizationStrategy(ILogger? logger = null)
    {
        _logger = logger;
    }

    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        settings.DisableNvarchar = false;

        if (optimizationSettings == null)
        {
            _logger?.LogDebug("应用虚谷数据库默认性能优化配置");
            return;
        }

        var applied = new List<string>();

        if (optimizationSettings.TryGetValue("maxPoolSize", out var maxPoolSizeStr) &&
            int.TryParse(maxPoolSizeStr, out var maxPoolSize))
        {
            _logger?.LogDebug("虚谷数据库最大连接池: {MaxPoolSize}", maxPoolSize);
            applied.Add($"maxPoolSize={maxPoolSize}");
        }

        if (applied.Count > 0)
        {
            _logger?.LogDebug("虚谷数据库优化配置选项: {Options}", string.Join(", ", applied));
        }

        _logger?.LogDebug("应用虚谷数据库性能优化配置");
    }

    public string GetDescription()
    {
        return "虚谷数据库优化：连接池提示";
    }
}