using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies.DBSetting;

/// <summary>
/// Doris 性能/兼容性策略（MySQL 兼容，但需禁用连接池）。
/// </summary>
public class DorisOptimizationStrategy : IDatabaseOptimizationStrategy
{
    private readonly ILogger? _logger;

    public DorisOptimizationStrategy(ILogger? logger = null)
    {
        _logger = logger;
    }

    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        // 默认不禁用 nvarchar；连接池建议在连接字符串配置 Pooling=false
        settings.DisableNvarchar = false;

        if (optimizationSettings == null)
        {
            _logger?.LogDebug("应用 Doris 默认性能优化配置");
            return;
        }

        if (optimizationSettings.TryGetValue("disableNvarchar", out var disableNvarcharStr) &&
            bool.TryParse(disableNvarcharStr, out var disableNvarchar))
        {
            settings.DisableNvarchar = disableNvarchar;
            _logger?.LogDebug("Doris 禁用 Nvarchar: {Disabled}", disableNvarchar);
        }

        _logger?.LogDebug("应用 Doris 性能优化配置");
    }

    public string GetDescription()
    {
        return "Doris 优化：MySQL 兼容 + 建议禁用连接池 + 可选禁用 Nvarchar/连接池参数提示";
    }
}
