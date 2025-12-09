using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies.DBSetting;

/// <summary>
/// MySQL / MariaDB / TiDB 性能优化策略
/// </summary>
public class MySqlOptimizationStrategy : IDatabaseOptimizationStrategy
{
    private readonly ILogger? _logger;

    public MySqlOptimizationStrategy(ILogger? logger = null)
    {
        _logger = logger;
    }

    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        // MySQL 不需要禁用 nvarchar
        settings.DisableNvarchar = false;

        if (optimizationSettings == null)
        {
            _logger?.LogDebug("应用 MySQL 默认性能优化配置");
            return;
        }

        var appliedOptions = new List<string>();

        // 是否启用 BulkCopy（需要连接字符串 AllowLoadLocalInfile=true）
        if (optimizationSettings.TryGetValue("enableBulkCopy", out var enableBulkCopyStr) &&
            bool.TryParse(enableBulkCopyStr, out var enableBulkCopy))
        {
            _logger?.LogDebug("MySQL 批量导入优化: {Enabled}", enableBulkCopy);
            appliedOptions.Add($"enableBulkCopy={enableBulkCopy}");
        }

        // 连接池大小
        if (optimizationSettings.TryGetValue("maxPoolSize", out var maxPoolSizeStr) &&
            int.TryParse(maxPoolSizeStr, out var maxPoolSize))
        {
            _logger?.LogDebug("MySQL 最大连接池: {MaxPoolSize}", maxPoolSize);
            appliedOptions.Add($"maxPoolSize={maxPoolSize}");
        }

        // 字符集
        if (optimizationSettings.TryGetValue("charset", out var charset))
        {
            _logger?.LogDebug("MySQL 字符集: {Charset}", charset);
            appliedOptions.Add($"charset={charset}");
        }

        // SSL 开关提示
        if (optimizationSettings.TryGetValue("enableSsl", out var enableSslStr) &&
            bool.TryParse(enableSslStr, out var enableSsl))
        {
            _logger?.LogDebug("MySQL SSL 连接: {Enabled}", enableSsl);
            appliedOptions.Add($"enableSsl={enableSsl}");
        }

        // 用户变量支持
        if (optimizationSettings.TryGetValue("allowUserVariables", out var allowUserVariablesStr) &&
            bool.TryParse(allowUserVariablesStr, out var allowUserVariables))
        {
            _logger?.LogDebug("MySQL 允许用户变量: {Enabled}", allowUserVariables);
            appliedOptions.Add($"allowUserVariables={allowUserVariables}");
        }

        if (appliedOptions.Count > 0)
        {
            _logger?.LogDebug("MySQL 优化配置选项: {Options}", string.Join(", ", appliedOptions));
        }

        _logger?.LogDebug("应用 MySQL 性能优化配置");
    }

    public string GetDescription()
    {
        return "MySQL 性能优化：utf8mb4 字符集 + 连接池 + BulkCopy 提示 + SSL/用户变量配置记录";
    }
}