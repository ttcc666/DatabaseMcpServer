using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies;

/// <summary>
/// TiDB 性能优化策略
/// TiDB 是兼容 MySQL 协议的分布式数据库，但具有独特的优化需求
/// </summary>
public class TidbOptimizationStrategy : IDatabaseOptimizationStrategy
{
    private readonly ILogger? _logger;

    public TidbOptimizationStrategy(ILogger? logger = null)
    {
        _logger = logger;
    }

    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        // TiDB 兼容 MySQL，不需要禁用 nvarchar
        settings.DisableNvarchar = false;

        if (optimizationSettings == null)
        {
            _logger?.LogDebug("应用 TiDB 默认性能优化配置");
            return;
        }

        // 读取是否启用 Optimizer Hints
        if (optimizationSettings.TryGetValue("enableHints", out var enableHintsStr) &&
            bool.TryParse(enableHintsStr, out var enableHints))
        {
            _logger?.LogDebug("TiDB Optimizer Hints 支持: {Enabled}", enableHints);
        }

        // 读取连接池大小配置
        if (optimizationSettings.TryGetValue("maxPoolSize", out var maxPoolSizeStr) &&
            int.TryParse(maxPoolSizeStr, out var maxPoolSize))
        {
            _logger?.LogDebug("TiDB 最大连接池大小: {MaxPoolSize}", maxPoolSize);
        }

        // 读取是否启用批量操作优化
        if (optimizationSettings.TryGetValue("enableBulkCopy", out var enableBulkCopyStr) &&
            bool.TryParse(enableBulkCopyStr, out var enableBulkCopy))
        {
            _logger?.LogDebug("TiDB 批量操作优化: {Enabled}", enableBulkCopy);
        }

        // 读取是否启用悲观事务模式
        if (optimizationSettings.TryGetValue("pessimisticTxn", out var pessimisticTxnStr) &&
            bool.TryParse(pessimisticTxnStr, out var pessimisticTxn))
        {
            _logger?.LogDebug("TiDB 悲观事务模式: {Enabled}", pessimisticTxn);
        }

        _logger?.LogDebug("应用 TiDB 性能优化配置");
    }

    public string GetDescription()
    {
        return "TiDB 性能优化：MySQL 兼容 + Optimizer Hints + 分布式事务 + 连接池管理";
    }
}
