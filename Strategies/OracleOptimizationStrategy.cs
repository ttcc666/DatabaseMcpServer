using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies;

/// <summary>
/// Oracle 性能优化策略
/// </summary>
public class OracleOptimizationStrategy : IDatabaseOptimizationStrategy
{
    private readonly ILogger? _logger;

    public OracleOptimizationStrategy(ILogger? logger = null)
    {
        _logger = logger;
    }

    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        // 默认转大写，可通过 JSON 配置覆盖
        settings.IsAutoToUpper = true;

        if (optimizationSettings == null) return;

        // 检查是否使用驼峰表名
        if (optimizationSettings.TryGetValue("camelCase", out var camelCaseStr) &&
            bool.TryParse(camelCaseStr, out var camelCase))
        {
            settings.IsAutoToUpper = !camelCase;
            _logger?.LogDebug("Oracle 使用驼峰表名: {CamelCase}", camelCase);
        }

        // Oracle 12C+ 原生自增支持
        if (optimizationSettings.TryGetValue("enableIdentity", out var enableIdentityStr) &&
            bool.TryParse(enableIdentityStr, out var enableIdentity))
        {
            settings.EnableOracleIdentity = enableIdentity;
            _logger?.LogDebug("Oracle 启用原生自增: {Enabled}", enableIdentity);
        }

        // Oracle 11 参数名长度限制
        if (optimizationSettings.TryGetValue("maxParamLength", out var maxParamLengthStr) &&
            int.TryParse(maxParamLengthStr, out var maxParamLength))
        {
            settings.MaxParameterNameLength = maxParamLength;
            _logger?.LogDebug("Oracle 参数名最大长度: {MaxLength}", maxParamLength);
        }

        _logger?.LogDebug("应用 Oracle 性能优化配置");
    }

    public string GetDescription()
    {
        return "Oracle 性能优化：大连接池 + 智能表名处理 + 原生自增支持";
    }
}