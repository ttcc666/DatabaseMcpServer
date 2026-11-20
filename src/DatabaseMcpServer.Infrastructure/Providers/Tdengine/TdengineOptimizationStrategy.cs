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
        // 默认配置即可，TDengine 主要依赖驱动/连接串选项（TsType、InstanceFactory 预热）。
        _logger?.LogDebug("应用 TDengine 配置：建议预热 InstanceFactory、检查 TsType（ms/us/ns）和 SDK 版本匹配");

        if (optimizationSettings is { Count: > 0 })
        {
            _logger?.LogDebug("TDengine 当前无可调 optimizationSettings，已忽略传入配置: {Keys}", string.Join(", ", optimizationSettings.Keys));
        }
    }

    public string GetDescription()
    {
        return "TDengine：原生高速连接 + ms/us/ns 精度 + 建议预热驱动/避免频繁 DDL";
    }
}
