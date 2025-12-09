using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies.DBSetting;

/// <summary>
/// IBM DB2 性能优化策略（轻量提示）
/// </summary>
public class Db2OptimizationStrategy : IDatabaseOptimizationStrategy
{
    private readonly ILogger? _logger;

    public Db2OptimizationStrategy(ILogger? logger = null)
    {
        _logger = logger;
    }

    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        settings.DisableNvarchar = false;

        if (optimizationSettings == null)
        {
            _logger?.LogDebug("应用 DB2 默认性能优化配置");
            return;
        }

        var applied = new List<string>();

        if (optimizationSettings.TryGetValue("maxPoolSize", out var maxPoolSizeStr) &&
            int.TryParse(maxPoolSizeStr, out var maxPoolSize))
        {
            _logger?.LogDebug("DB2 最大连接池: {MaxPoolSize}", maxPoolSize);
            applied.Add($"maxPoolSize={maxPoolSize}");
        }

        if (applied.Count > 0)
        {
            _logger?.LogDebug("DB2 优化配置选项: {Options}", string.Join(", ", applied));
        }

        _logger?.LogDebug("应用 DB2 性能优化配置");
    }

    public string GetDescription()
    {
        return "DB2 优化：连接池提示";
    }
}