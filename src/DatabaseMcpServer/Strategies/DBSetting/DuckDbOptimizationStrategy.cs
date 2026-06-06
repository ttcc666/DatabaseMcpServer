using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies.DBSetting;

/// <summary>
/// DuckDB 性能/兼容性策略（提示型，主要依赖驱动/连接串）。
/// </summary>
public class DuckDbOptimizationStrategy : IDatabaseOptimizationStrategy
{
    private readonly ILogger? _logger;

    public DuckDbOptimizationStrategy(ILogger? logger = null)
    {
        _logger = logger;
    }

    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        // DuckDB 为嵌入式/单机 OLAP，常规配置即可。
        _logger?.LogDebug("应用 DuckDB 配置：建议预热 Provider，避免频繁 DDL，专注读取/聚合场景");

        if (optimizationSettings is { Count: > 0 })
        {
            _logger?.LogDebug("DuckDB 当前无可调 optimizationSettings，已忽略传入配置: {Keys}", string.Join(", ", optimizationSettings.Keys));
        }
    }

    public string GetDescription()
    {
        return "DuckDB：嵌入式 OLAP，建议预热 Provider，避免频繁 DDL，聚焦读取/聚合";
    }
}
