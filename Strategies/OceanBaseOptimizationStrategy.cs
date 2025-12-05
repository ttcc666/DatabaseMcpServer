using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies;

/// <summary>
/// OceanBase MySQL 模式性能优化策略
/// OceanBase 兼容 MySQL 协议，但有特殊的连接池和事务处理需求
/// </summary>
public class OceanBaseOptimizationStrategy : IDatabaseOptimizationStrategy
{
    private readonly ILogger? _logger;

    public OceanBaseOptimizationStrategy(ILogger? logger = null)
    {
        _logger = logger;
    }

    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        // OceanBase 兼容 MySQL，不需要禁用 nvarchar
        settings.DisableNvarchar = false;

        if (optimizationSettings == null)
        {
            _logger?.LogDebug("应用 OceanBase 默认性能优化配置");
            return;
        }

        // 读取是否禁用连接池（某些 OceanBase 服务器不支持连接池）
        if (optimizationSettings.TryGetValue("disablePooling", out var disablePoolingStr) &&
            bool.TryParse(disablePoolingStr, out var disablePooling))
        {
            _logger?.LogDebug("OceanBase 禁用连接池: {Disabled} (某些服务器连续写入操作可能需要禁用连接池)", disablePooling);
        }

        // 读取连接池大小配置
        if (optimizationSettings.TryGetValue("maxPoolSize", out var maxPoolSizeStr) &&
            int.TryParse(maxPoolSizeStr, out var maxPoolSize))
        {
            _logger?.LogDebug("OceanBase 最大连接池大小: {MaxPoolSize}", maxPoolSize);
        }

        // 读取是否启用 Optimizer Hints
        if (optimizationSettings.TryGetValue("enableHints", out var enableHintsStr) &&
            bool.TryParse(enableHintsStr, out var enableHints))
        {
            _logger?.LogDebug("OceanBase Optimizer Hints 支持: {Enabled}", enableHints);
        }

        // 读取是否启用批量操作优化
        if (optimizationSettings.TryGetValue("enableBulkCopy", out var enableBulkCopyStr) &&
            bool.TryParse(enableBulkCopyStr, out var enableBulkCopy))
        {
            _logger?.LogDebug("OceanBase 批量操作优化: {Enabled}", enableBulkCopy);
        }

        // 读取租户模式配置
        if (optimizationSettings.TryGetValue("tenantMode", out var tenantMode))
        {
            _logger?.LogDebug("OceanBase 租户模式: {TenantMode}", tenantMode);
        }

        _logger?.LogDebug("应用 OceanBase 性能优化配置");
    }

    public string GetDescription()
    {
        return "OceanBase 性能优化：MySQL 兼容 + 连接池管理 + Optimizer Hints + 租户隔离";
    }
}
