using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies;

/// <summary>
/// TDengine 时序数据库性能优化策略
/// </summary>
public class TdengineOptimizationStrategy : IDatabaseOptimizationStrategy
{
    private readonly ILogger? _logger;

    public TdengineOptimizationStrategy(ILogger? logger = null)
    {
        _logger = logger;
    }

    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        // TDengine 使用自有引擎，保持默认设置；无 nvarchar 需求
        settings.DisableNvarchar = true;

        var appliedOptions = new List<string>();

        if (optimizationSettings == null)
        {
            _logger?.LogDebug("应用 TDengine 默认性能优化配置");
            return;
        }

        // 连接池大小提示
        if (optimizationSettings.TryGetValue("maxPoolSize", out var maxPoolSizeStr) &&
            int.TryParse(maxPoolSizeStr, out var maxPoolSize))
        {
            _logger?.LogDebug("TDengine 最大连接池: {MaxPoolSize}", maxPoolSize);
            appliedOptions.Add($"maxPoolSize={maxPoolSize}");
        }

        // 批量写入批次提示（文档强调大数据写入）
        if (optimizationSettings.TryGetValue("batchSize", out var batchSizeStr) &&
            int.TryParse(batchSizeStr, out var batchSize))
        {
            _logger?.LogDebug("TDengine 批量写入批次: {BatchSize}", batchSize);
            appliedOptions.Add($"batchSize={batchSize}");
        }

        if (appliedOptions.Count > 0)
        {
            _logger?.LogDebug("TDengine 优化配置选项: {Options}", string.Join(", ", appliedOptions));
        }

        _logger?.LogDebug("应用 TDengine 性能优化配置");
    }

    public string GetDescription()
    {
        return "TDengine 优化：禁用 nvarchar + 连接池/批量写入提示";
    }
}