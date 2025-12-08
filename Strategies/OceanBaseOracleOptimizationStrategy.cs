using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies;

/// <summary>
/// OceanBase Oracle 模式性能优化策略（独立于 Oracle 策略，避免混用）
/// </summary>
public class OceanBaseOracleOptimizationStrategy : IDatabaseOptimizationStrategy
{
    private readonly ILogger? _logger;

    public OceanBaseOracleOptimizationStrategy(ILogger? logger = null)
    {
        _logger = logger;
    }

    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        // 默认转大写，保持与 Oracle 兼容模式一致
        settings.IsAutoToUpper = true;

        if (optimizationSettings == null)
        {
            _logger?.LogDebug("应用 OceanBase Oracle 模式默认性能优化配置");
            return;
        }

        var appliedOptions = new List<string>();

        // 使用驼峰表名（不自动转大写）
        if (optimizationSettings.TryGetValue("camelCase", out var camelCaseStr) &&
            bool.TryParse(camelCaseStr, out var camelCase))
        {
            settings.IsAutoToUpper = !camelCase;
            _logger?.LogDebug("OceanBase Oracle 模式使用驼峰表名: {CamelCase}", camelCase);
            appliedOptions.Add($"camelCase={camelCase}");
        }

        // 原生自增支持（12C+）
        if (optimizationSettings.TryGetValue("enableIdentity", out var enableIdentityStr) &&
            bool.TryParse(enableIdentityStr, out var enableIdentity))
        {
            settings.EnableOracleIdentity = enableIdentity;
            _logger?.LogDebug("OceanBase Oracle 模式启用原生自增: {Enabled}", enableIdentity);
            appliedOptions.Add($"enableIdentity={enableIdentity}");
        }

        if (appliedOptions.Count > 0)
        {
            _logger?.LogDebug("OceanBase Oracle 模式优化配置选项: {Options}", string.Join(", ", appliedOptions));
        }

        _logger?.LogDebug("应用 OceanBase Oracle 模式性能优化配置");
    }

    public string GetDescription()
    {
        return "OceanBase Oracle 模式优化：表名大小写处理 + 原生自增支持";
    }
}