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
        // 默认不禁用 nvarchar；连接池建议禁用（Pooling=false）
        settings.DisableNvarchar = false;

        if (optimizationSettings == null)
        {
            _logger?.LogWarning("Doris 建议在连接字符串设置 Pooling=false（特殊网络/驱动兼容）");
            return;
        }

        var appliedOptions = new List<string>();

        if (optimizationSettings.TryGetValue("disablePooling", out var disablePoolingStr) &&
            bool.TryParse(disablePoolingStr, out var disablePooling))
        {
            if (!disablePooling)
            {
                _logger?.LogWarning("Doris 建议禁用连接池：在连接字符串设置 Pooling=false");
            }
            appliedOptions.Add($"disablePooling={disablePooling}");
        }
        else
        {
            _logger?.LogWarning("Doris 未显式配置 disablePooling，推荐 Pooling=false");
        }

        if (optimizationSettings.TryGetValue("disableNvarchar", out var disableNvarcharStr) &&
            bool.TryParse(disableNvarcharStr, out var disableNvarchar))
        {
            settings.DisableNvarchar = disableNvarchar;
            appliedOptions.Add($"disableNvarchar={disableNvarchar}");
            _logger?.LogDebug("Doris 禁用 Nvarchar: {Disabled}", disableNvarchar);
        }

        if (optimizationSettings.TryGetValue("maxPoolSize", out var maxPoolSizeStr) &&
            int.TryParse(maxPoolSizeStr, out var maxPoolSize))
        {
            appliedOptions.Add($"maxPoolSize={maxPoolSize}");
            _logger?.LogDebug("Doris 最大连接池: {MaxPoolSize}", maxPoolSize);
        }

        if (appliedOptions.Count > 0)
        {
            _logger?.LogDebug("Doris 优化配置选项: {Options}", string.Join(", ", appliedOptions));
        }

        _logger?.LogDebug("应用 Doris 性能优化配置");
    }

    public string GetDescription()
    {
        return "Doris 优化：MySQL 兼容 + 建议禁用连接池 + 可选禁用 Nvarchar/连接池参数提示";
    }
}
