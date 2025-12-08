using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies;

/// <summary>
/// SAP HANA 性能优化策略（轻量提示）
/// </summary>
public class HanaOptimizationStrategy : IDatabaseOptimizationStrategy
{
    private readonly ILogger? _logger;

    public HanaOptimizationStrategy(ILogger? logger = null)
    {
        _logger = logger;
    }

    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        // 默认保持 nvarchar 支持
        settings.DisableNvarchar = false;

        if (optimizationSettings == null)
        {
            _logger?.LogDebug("应用 SAP HANA 默认性能优化配置");
            return;
        }

        var applied = new List<string>();

        if (optimizationSettings.TryGetValue("maxPoolSize", out var maxPoolSizeStr) &&
            int.TryParse(maxPoolSizeStr, out var maxPoolSize))
        {
            _logger?.LogDebug("SAP HANA 最大连接池: {MaxPoolSize}", maxPoolSize);
            applied.Add($"maxPoolSize={maxPoolSize}");
        }

        if (applied.Count > 0)
        {
            _logger?.LogDebug("SAP HANA 优化配置选项: {Options}", string.Join(", ", applied));
        }

        _logger?.LogDebug("应用 SAP HANA 性能优化配置");
    }

    public string GetDescription()
    {
        return "SAP HANA 优化：连接池提示";
    }
}