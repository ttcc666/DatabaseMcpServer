using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies.DBSetting;

/// <summary>
/// 默认数据库性能优化策略（用于未特别配置的数据库类型）
/// </summary>
public class DefaultOptimizationStrategy : IDatabaseOptimizationStrategy
{
    private readonly ILogger? _logger;
    private readonly string _dbTypeName;

    public DefaultOptimizationStrategy(string dbTypeName, ILogger? logger = null)
    {
        _dbTypeName = dbTypeName;
        _logger = logger;
    }

    /// <summary>
    /// 应用默认性能配置（无特定调优）。
    /// </summary>
    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        // 使用默认配置，不做特殊优化
        _logger?.LogDebug("使用默认性能配置: {DbType}", _dbTypeName);
    }

    /// <summary>
    /// 获取默认策略描述。
    /// </summary>
    public string GetDescription()
    {
        return $"{_dbTypeName} 默认配置：通用性能优化";
    }
}