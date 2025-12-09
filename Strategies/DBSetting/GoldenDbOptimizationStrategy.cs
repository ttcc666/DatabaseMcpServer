using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies.DBSetting;

/// <summary>
/// GoldenDB（MySQL 兼容）性能优化策略
/// </summary>
public class GoldenDbOptimizationStrategy : IDatabaseOptimizationStrategy
{
    private readonly ILogger? _logger;

    public GoldenDbOptimizationStrategy(ILogger? logger = null)
    {
        _logger = logger;
    }

    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        // MySQL 兼容，不禁用 nvarchar
        settings.DisableNvarchar = false;

        if (optimizationSettings == null)
        {
            _logger?.LogDebug("应用 GoldenDB 默认性能优化配置");
            return;
        }

        var applied = new List<string>();

        // GoldenDB 官方建议连接串禁用连接池（Pooling=false）
        if (!optimizationSettings.TryGetValue("disablePooling", out var disablePoolingStr) ||
            !bool.TryParse(disablePoolingStr, out var disablePooling) ||
            !disablePooling)
        {
            _logger?.LogWarning("GoldenDB 建议在连接字符串中设置 Pooling=false 以避免兼容性问题");
        }
        else
        {
            applied.Add($"disablePooling={disablePooling}");
        }

        if (optimizationSettings.TryGetValue("maxPoolSize", out var maxPoolSizeStr) &&
            int.TryParse(maxPoolSizeStr, out var maxPoolSize))
        {
            _logger?.LogDebug("GoldenDB 最大连接池: {MaxPoolSize}", maxPoolSize);
            applied.Add($"maxPoolSize={maxPoolSize}");
        }

        if (applied.Count > 0)
        {
            _logger?.LogDebug("GoldenDB 优化配置选项: {Options}", string.Join(", ", applied));
        }

        _logger?.LogDebug("应用 GoldenDB 性能优化配置");
    }

    public string GetDescription()
    {
        return "GoldenDB 优化：MySQL 兼容 + 连接池提示";
    }
}