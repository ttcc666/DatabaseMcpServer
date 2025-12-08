using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies;

/// <summary>
/// 瀚高数据库（HighGo）性能优化策略
/// </summary>
public class HighGoOptimizationStrategy : IDatabaseOptimizationStrategy
{
    private readonly ILogger? _logger;

    public HighGoOptimizationStrategy(ILogger? logger = null)
    {
        _logger = logger;
    }

    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        // HighGo 基于 PostgreSQL，默认表名转小写
        settings.PgSqlIsAutoToLower = true;

        if (optimizationSettings == null)
        {
            _logger?.LogDebug("应用瀚高数据库默认性能优化配置");
            return;
        }

        var applied = new List<string>();

        if (optimizationSettings.TryGetValue("autoToLower", out var autoToLowerStr) &&
            bool.TryParse(autoToLowerStr, out var autoToLower))
        {
            settings.PgSqlIsAutoToLower = autoToLower;
            _logger?.LogDebug("瀚高数据库表名自动转小写: {Enabled}", autoToLower);
            applied.Add($"autoToLower={autoToLower}");
        }

        if (optimizationSettings.TryGetValue("maxPoolSize", out var maxPoolSizeStr) &&
            int.TryParse(maxPoolSizeStr, out var maxPoolSize))
        {
            _logger?.LogDebug("瀚高数据库最大连接池: {MaxPoolSize}", maxPoolSize);
            applied.Add($"maxPoolSize={maxPoolSize}");
        }

        if (applied.Count > 0)
        {
            _logger?.LogDebug("瀚高数据库优化配置选项: {Options}", string.Join(", ", applied));
        }

        _logger?.LogDebug("应用瀚高数据库性能优化配置");
    }

    public string GetDescription()
    {
        return "瀚高数据库优化：PG 兼容表名小写 + 连接池提示";
    }
}