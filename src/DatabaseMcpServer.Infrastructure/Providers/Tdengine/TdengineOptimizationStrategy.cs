using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies.DBSetting;

/// <summary>
/// TDengine 性能/兼容性策略（提示型）。
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
        settings.PgSqlIsAutoToLower = true;
        if (optimizationSettings == null)
        {
            _logger?.LogDebug("应用 TDengine 默认配置");
            return;
        }

        if (optimizationSettings.TryGetValue("autoToLower", out var autoToLowerStr) &&
            bool.TryParse(autoToLowerStr, out var autoToLower))
            settings.PgSqlIsAutoToLower = autoToLower;

        if (optimizationSettings.TryGetValue("autoToLowerCodeFirst", out var autoToLowerCodeFirstStr) &&
            bool.TryParse(autoToLowerCodeFirstStr, out var autoToLowerCodeFirst))
            settings.PgSqlIsAutoToLowerCodeFirst = autoToLowerCodeFirst;
    }

    public string GetDescription()
    {
        return "TDengine：原生高速连接 + ms/us/ns 精度 + 建议预热驱动/避免频繁 DDL";
    }
}
