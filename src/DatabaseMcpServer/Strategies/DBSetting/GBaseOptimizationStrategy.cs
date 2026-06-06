using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies.DBSetting;

/// <summary>
/// GBase 8s 性能/兼容性策略（ODBC 驱动为主）。
/// </summary>
public class GBaseOptimizationStrategy : IDatabaseOptimizationStrategy
{
    private readonly ILogger? _logger;

    public GBaseOptimizationStrategy(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 应用 GBase 8s 优化配置。
    /// </summary>
    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        // 默认保持 nvarchar 支持，由连接串/驱动决定。暂未暴露特殊开关，仅做提示日志。
        if (optimizationSettings is { Count: > 0 })
        {
            _logger?.LogDebug("GBase 当前无可调 optimizationSettings，已忽略传入配置: {Keys}", string.Join(", ", optimizationSettings.Keys));
        }

        _logger?.LogDebug("应用 GBase 8s 配置：建议使用 ODBC 驱动 + SqlSugar.GBaseCore 5.1.4.170+，避免频繁 DDL");
    }

    /// <summary>
    /// 获取 GBase 优化策略描述。
    /// </summary>
    public string GetDescription()
    {
        return "GBase 8s：ODBC 驱动，建议 SqlSugar.GBaseCore 5.1.4.170+，常规 CRUD/分页插入";
    }
}
