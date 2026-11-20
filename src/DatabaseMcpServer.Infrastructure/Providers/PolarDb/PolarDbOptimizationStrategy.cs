using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies.DBSetting;

/// <summary>
/// PolarDB 性能优化策略（MySQL 兼容，但独立配置）。
/// </summary>
public class PolarDbOptimizationStrategy : IDatabaseOptimizationStrategy
{
    private readonly ILogger? _logger;

    public PolarDbOptimizationStrategy(ILogger? logger = null)
    {
        _logger = logger;
    }

    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        // 默认不禁用 nvarchar；允许后续按需关闭
        settings.DisableNvarchar = false;

        if (optimizationSettings == null)
        {
            _logger?.LogDebug("应用 PolarDB 默认性能优化配置");
            return;
        }

        var appliedOptions = new List<string>();

        if (optimizationSettings.TryGetValue("disableNvarchar", out var disableNvarcharStr) &&
            bool.TryParse(disableNvarcharStr, out var disableNvarchar))
        {
            settings.DisableNvarchar = disableNvarchar;
            appliedOptions.Add($"disableNvarchar={disableNvarchar}");
            _logger?.LogDebug("PolarDB 禁用 Nvarchar: {Disabled}", disableNvarchar);
        }

        if (optimizationSettings.TryGetValue("enableBulkCopy", out var enableBulkCopyStr) &&
            bool.TryParse(enableBulkCopyStr, out var enableBulkCopy))
        {
            appliedOptions.Add($"enableBulkCopy={enableBulkCopy}");
            _logger?.LogDebug("PolarDB 批量导入优化: {Enabled}", enableBulkCopy);
        }

        if (optimizationSettings.TryGetValue("maxPoolSize", out var maxPoolSizeStr) &&
            int.TryParse(maxPoolSizeStr, out var maxPoolSize))
        {
            appliedOptions.Add($"maxPoolSize={maxPoolSize}");
            _logger?.LogDebug("PolarDB 最大连接池: {MaxPoolSize}", maxPoolSize);
        }

        if (optimizationSettings.TryGetValue("charset", out var charset))
        {
            appliedOptions.Add($"charset={charset}");
            _logger?.LogDebug("PolarDB 字符集: {Charset}", charset);
        }

        if (optimizationSettings.TryGetValue("enableSsl", out var enableSslStr) &&
            bool.TryParse(enableSslStr, out var enableSsl))
        {
            appliedOptions.Add($"enableSsl={enableSsl}");
            _logger?.LogDebug("PolarDB SSL 连接: {Enabled}", enableSsl);
        }

        if (optimizationSettings.TryGetValue("allowUserVariables", out var allowUserVariablesStr) &&
            bool.TryParse(allowUserVariablesStr, out var allowUserVariables))
        {
            appliedOptions.Add($"allowUserVariables={allowUserVariables}");
            _logger?.LogDebug("PolarDB 允许用户变量: {Enabled}", allowUserVariables);
        }

        if (appliedOptions.Count > 0)
        {
            _logger?.LogDebug("PolarDB 优化配置选项: {Options}", string.Join(", ", appliedOptions));
        }

        _logger?.LogDebug("应用 PolarDB 性能优化配置");
    }

    public string GetDescription()
    {
        return "PolarDB 性能优化：MySQL 兼容 + 连接池/BulkCopy/字符集/SSL/用户变量 + 可选禁用 Nvarchar";
    }
}
