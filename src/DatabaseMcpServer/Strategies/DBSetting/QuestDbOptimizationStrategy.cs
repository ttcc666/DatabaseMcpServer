using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies.DBSetting;

/// <summary>
/// QuestDB 性能/安全提示策略
/// </summary>
public class QuestDbOptimizationStrategy : IDatabaseOptimizationStrategy
{
    private readonly ILogger? _logger;

    public QuestDbOptimizationStrategy(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 应用 QuestDB 优化配置（仅提示性质，避免 DDL/Truncate 频繁锁表）。
    /// </summary>
    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        // 默认保持 SqlSugar 基本配置；QuestDB 不建议频繁 DDL/Truncate
        _logger?.LogDebug("应用 QuestDB 性能提示：避免频繁 DDL/Truncate，建议只做追加写入和聚合查询");

        if (optimizationSettings is { Count: > 0 })
        {
            _logger?.LogDebug("QuestDB 当前未提供可调优的 optimizationSettings，已忽略传入配置: {Keys}", string.Join(", ", optimizationSettings.Keys));
        }
    }

    /// <summary>
    /// 获取 QuestDB 优化策略描述。
    /// </summary>
    public string GetDescription()
    {
        return "QuestDB 提示：高性能追加写入 + 避免频繁 DDL/Truncate + 优先使用聚合/分区查询";
    }
}
