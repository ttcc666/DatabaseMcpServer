using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies.DBSetting;

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

    /// <summary>
    /// 应用 Vastbase 优化配置（PG 兼容，小写表名、连接池提示、NoResetOnClose 建议）。
    /// </summary>
    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        // 默认表名转小写，贴合 PG 生态
        settings.PgSqlIsAutoToLower = true;
        // 默认不禁用 nvarchar，可按需关闭
        settings.DisableNvarchar = false;

        if (optimizationSettings == null)
        {
            _logger?.LogDebug("应用 Vastbase 默认性能优化配置");
            return;
        }

        if (optimizationSettings.TryGetValue("autoToLower", out var autoToLowerStr) &&
            bool.TryParse(autoToLowerStr, out var autoToLower))
        {
            settings.PgSqlIsAutoToLower = autoToLower;
            _logger?.LogDebug("Vastbase 表名自动转小写: {Enabled}", autoToLower);
        }

        if (optimizationSettings.TryGetValue("autoToLowerCodeFirst", out var autoToLowerCodeFirstStr) &&
            bool.TryParse(autoToLowerCodeFirstStr, out var autoToLowerCodeFirst))
        {
            settings.PgSqlIsAutoToLowerCodeFirst = autoToLowerCodeFirst;
            _logger?.LogDebug("Vastbase CodeFirst 表名自动转小写: {Enabled}", autoToLowerCodeFirst);
        }

        // 可选禁用 nvarchar（少数版本需要）
        if (optimizationSettings.TryGetValue("disableNvarchar", out var disableNvarcharStr) &&
            bool.TryParse(disableNvarcharStr, out var disableNvarchar))
        {
            settings.DisableNvarchar = disableNvarchar;
            _logger?.LogDebug("Vastbase 禁用 Nvarchar: {Disabled}", disableNvarchar);
        }

        _logger?.LogDebug("应用 Vastbase 性能优化配置");
    }

    /// <summary>
    /// 获取 Vastbase 优化策略描述。
    /// </summary>
    public string GetDescription()
    {
        return "Vastbase 优化：PG 兼容表名小写 + 连接池提示";
    }
}
