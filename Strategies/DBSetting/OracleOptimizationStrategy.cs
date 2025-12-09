using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies.DBSetting;

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

    /// <summary>
    /// 应用 Oracle 优化配置（表名大小写、自增、参数名长度等）。
    /// </summary>
    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        // 默认转大写，可通过 JSON 配置覆盖；默认不禁用 nvarchar
        settings.IsAutoToUpper = true;
        settings.DisableNvarchar = false;

        if (optimizationSettings == null)
        {
            _logger?.LogDebug("应用 Oracle 默认性能优化配置（自动转大写，未禁用 Nvarchar）");
            return;
        }

        var appliedOptions = new List<string>();

        // 检查是否使用驼峰表名
        if (optimizationSettings.TryGetValue("camelCase", out var camelCaseStr) &&
            bool.TryParse(camelCaseStr, out var camelCase))
        {
            settings.IsAutoToUpper = !camelCase;
            appliedOptions.Add($"camelCase={camelCase}");
            _logger?.LogDebug("Oracle 使用驼峰表名: {CamelCase}", camelCase);
        }

        // Oracle 12C+ 原生自增支持
        if (optimizationSettings.TryGetValue("enableIdentity", out var enableIdentityStr) &&
            bool.TryParse(enableIdentityStr, out var enableIdentity))
        {
            settings.EnableOracleIdentity = enableIdentity;
            appliedOptions.Add($"enableIdentity={enableIdentity}");
            _logger?.LogDebug("Oracle 启用原生自增: {Enabled}", enableIdentity);
        }

        // Oracle 11 参数名长度限制
        if (optimizationSettings.TryGetValue("maxParamLength", out var maxParamLengthStr) &&
            int.TryParse(maxParamLengthStr, out var maxParamLength))
        {
            settings.MaxParameterNameLength = maxParamLength;
            appliedOptions.Add($"maxParamLength={maxParamLength}");
            _logger?.LogDebug("Oracle 参数名最大长度: {MaxLength}", maxParamLength);
        }

        // 可选禁用 nvarchar（针对特殊字符/索引兼容场景）
        if (optimizationSettings.TryGetValue("disableNvarchar", out var disableNvarcharStr) &&
            bool.TryParse(disableNvarcharStr, out var disableNvarchar))
        {
            settings.DisableNvarchar = disableNvarchar;
            appliedOptions.Add($"disableNvarchar={disableNvarchar}");
            _logger?.LogDebug("Oracle 禁用 Nvarchar: {Disabled}", disableNvarchar);
        }

        if (appliedOptions.Count > 0)
        {
            _logger?.LogDebug("Oracle 优化配置选项: {Options}", string.Join(", ", appliedOptions));
        }

        _logger?.LogDebug("应用 Oracle 性能优化配置");
    }

    /// <summary>
    /// 获取 Oracle 优化策略描述。
    /// </summary>
    public string GetDescription()
    {
        return "Oracle 性能优化：大连接池 + 智能表名处理 + 原生自增支持";
    }
}
