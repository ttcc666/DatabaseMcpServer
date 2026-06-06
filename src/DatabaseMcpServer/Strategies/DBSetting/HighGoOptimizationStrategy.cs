using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies.DBSetting;

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

    /// <summary>
    /// 应用瀚高数据库优化配置（PG 兼容，小写表名、连接池提示）。
    /// </summary>
    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        // HighGo 基于 PostgreSQL，默认表名转小写
        settings.PgSqlIsAutoToLower = true;

        if (optimizationSettings == null)
        {
            _logger?.LogDebug("应用瀚高数据库默认性能优化配置（推荐 Pooling=false，表名转小写）");
            _logger?.LogWarning("HighGo 建议在连接字符串添加 Pooling=false 以规避特殊网络/驱动兼容问题");
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

        if (optimizationSettings.TryGetValue("disablePooling", out var disablePoolingStr) &&
            bool.TryParse(disablePoolingStr, out var disablePooling))
        {
            if (!disablePooling)
            {
                _logger?.LogWarning("HighGo 建议禁用连接池 (Pooling=false) 以避免 Unsupported command 等兼容问题");
            }
            applied.Add($"disablePooling={disablePooling}");
        }
        else
        {
            _logger?.LogWarning("HighGo 未显式配置 disablePooling，推荐在连接字符串设置 Pooling=false");
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

    /// <summary>
    /// 获取瀚高数据库优化策略描述。
    /// </summary>
    public string GetDescription()
    {
        return "瀚高数据库优化：PG 兼容表名小写 + 连接池提示";
    }
}
