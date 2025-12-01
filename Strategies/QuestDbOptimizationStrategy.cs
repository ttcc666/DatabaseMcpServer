using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies;

/// <summary>
/// QuestDB 时序数据库性能优化策略
/// </summary>
public class QuestDbOptimizationStrategy : IDatabaseOptimizationStrategy
{
    private readonly ILogger? _logger;

    public QuestDbOptimizationStrategy(ILogger? logger = null)
    {
        _logger = logger;
    }

    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        // QuestDB 时序数据库特定优化配置

        // 1. 禁用 nvarchar（QuestDB 使用 PostgreSQL 协议，但不需要 Unicode 类型）
        settings.DisableNvarchar = true;

        if (optimizationSettings == null) return;

        // 2. 检查是否启用 WAL 同步写入（默认异步以提高性能）
        if (optimizationSettings.TryGetValue("syncWal", out var syncWalStr) &&
            bool.TryParse(syncWalStr, out var syncWal))
        {
            _logger?.LogDebug("QuestDB WAL 同步写入: {Enabled}", syncWal);
            // 注意：QuestDB 默认异步写入以提高性能，同步写入会降低性能但提高数据一致性
        }

        // 3. Symbol 类型优化提示（通过日志记录）
        if (optimizationSettings.TryGetValue("symbolOptimization", out var symbolOptimizationStr) &&
            bool.TryParse(symbolOptimizationStr, out var symbolOptimization))
        {
            if (symbolOptimization)
            {
                _logger?.LogInformation("QuestDB Symbol 类型优化已启用：确保高重复率字段使用 symbol 类型，去重后应小于 60k");
            }
        }

        // 4. 批量插入优化
        if (optimizationSettings.TryGetValue("batchSize", out var batchSizeStr) &&
            int.TryParse(batchSizeStr, out var batchSize))
        {
            _logger?.LogDebug("QuestDB 批量插入大小: {BatchSize}", batchSize);
            // QuestDB 推荐使用批量插入以提高性能
        }

        // 5. 分区策略提示
        if (optimizationSettings.TryGetValue("partitionStrategy", out var partitionStrategy))
        {
            _logger?.LogDebug("QuestDB 分区策略: {Strategy}", partitionStrategy);
            // QuestDB 支持按时间分区（DAY, MONTH, YEAR）
        }

        _logger?.LogDebug("应用 QuestDB 时序数据库性能优化配置");
    }

    public string GetDescription()
    {
        return "QuestDB 时序数据库优化：WAL 异步写入 + Symbol 类型优化 + 批量插入 + 时间分区";
    }
}
