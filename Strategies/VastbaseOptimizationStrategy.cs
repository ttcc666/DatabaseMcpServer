using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies;

/// <summary>
/// Vastbase（PostgreSQL 衍生）性能优化策略
/// </summary>
public class VastbaseOptimizationStrategy : IDatabaseOptimizationStrategy
{
    private readonly ILogger? _logger;

    public VastbaseOptimizationStrategy(ILogger? logger = null)
    {
        _logger = logger;
    }

    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        // 默认表名转小写，贴合 PG 生态
        settings.PgSqlIsAutoToLower = true;

        if (optimizationSettings == null)
        {
            _logger?.LogDebug("应用 Vastbase 默认性能优化配置");
            _logger?.LogInformation("Vastbase 建议在连接字符串添加 No Reset On Close=true 以兼容 PG 驱动");
            return;
        }

        var applied = new List<string>();

        if (optimizationSettings.TryGetValue("autoToLower", out var autoToLowerStr) &&
            bool.TryParse(autoToLowerStr, out var autoToLower))
        {
            settings.PgSqlIsAutoToLower = autoToLower;
            _logger?.LogDebug("Vastbase 表名自动转小写: {Enabled}", autoToLower);
            applied.Add($"autoToLower={autoToLower}");
        }

        if (optimizationSettings.TryGetValue("maxPoolSize", out var maxPoolSizeStr) &&
            int.TryParse(maxPoolSizeStr, out var maxPoolSize))
        {
            _logger?.LogDebug("Vastbase 最大连接池: {MaxPoolSize}", maxPoolSize);
            applied.Add($"maxPoolSize={maxPoolSize}");
        }

        if (optimizationSettings.TryGetValue("noResetOnClose", out var noResetStr) &&
            bool.TryParse(noResetStr, out var noResetOnClose))
        {
            if (!noResetOnClose)
            {
                _logger?.LogWarning("Vastbase 建议将连接字符串 No Reset On Close=true 以避免连接复用问题");
            }
            applied.Add($"noResetOnClose={noResetOnClose}");
        }
        else
        {
            _logger?.LogInformation("Vastbase 未显式设置 No Reset On Close，建议在连接字符串中添加 No Reset On Close=true");
        }

        if (applied.Count > 0)
        {
            _logger?.LogDebug("Vastbase 优化配置选项: {Options}", string.Join(", ", applied));
        }

        _logger?.LogDebug("应用 Vastbase 性能优化配置");
    }

    public string GetDescription()
    {
        return "Vastbase 优化：PG 兼容表名小写 + 连接池提示";
    }
}