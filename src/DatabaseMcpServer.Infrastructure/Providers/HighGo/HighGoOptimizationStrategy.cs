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
            _logger?.LogDebug("应用瀚高数据库默认性能优化配置");
            return;
        }

        if (optimizationSettings.TryGetValue("autoToLower", out var autoToLowerStr) &&
            bool.TryParse(autoToLowerStr, out var autoToLower))
        {
            settings.PgSqlIsAutoToLower = autoToLower;
            _logger?.LogDebug("瀚高数据库表名自动转小写: {Enabled}", autoToLower);
        }

        if (optimizationSettings.TryGetValue("autoToLowerCodeFirst", out var autoToLowerCodeFirstStr) &&
            bool.TryParse(autoToLowerCodeFirstStr, out var autoToLowerCodeFirst))
        {
            settings.PgSqlIsAutoToLowerCodeFirst = autoToLowerCodeFirst;
            _logger?.LogDebug("瀚高数据库 CodeFirst 表名自动转小写: {Enabled}", autoToLowerCodeFirst);
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
