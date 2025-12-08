using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies;

/// <summary>
/// GBase 8s 数据库性能优化策略
/// </summary>
public class GBaseOptimizationStrategy : IDatabaseOptimizationStrategy
{
    private readonly ILogger? _logger;

    public GBaseOptimizationStrategy(ILogger? logger = null)
    {
        _logger = logger;
    }

    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        // GBase 兼容 MySQL/ODBC 协议，默认不禁用 nvarchar
        settings.DisableNvarchar = false;

        if (optimizationSettings == null)
        {
            _logger?.LogDebug("应用 GBase 默认性能优化配置");
            return;
        }

        var appliedOptions = new List<string>();

        // GBase 不支持 BulkCopy，提示使用分页批量插入
        if (optimizationSettings.TryGetValue("enableBulkCopy", out var enableBulkCopyStr) &&
            bool.TryParse(enableBulkCopyStr, out var enableBulkCopy))
        {
            if (enableBulkCopy)
            {
                _logger?.LogWarning("GBase 不支持 BulkCopy，请改用 Insertable(...).PageSize(...) 进行分页批量写入");
            }

            appliedOptions.Add($"enableBulkCopy={enableBulkCopy}");
        }

        // 批量插入分页大小（Insertable().PageSize()）
        if (optimizationSettings.TryGetValue("batchPageSize", out var batchPageSizeStr) &&
            int.TryParse(batchPageSizeStr, out var batchPageSize))
        {
            _logger?.LogDebug("GBase 批量插入分页大小: {BatchPageSize}", batchPageSize);
            appliedOptions.Add($"batchPageSize={batchPageSize}");
        }

        // Locale 配置提醒
        if (optimizationSettings.TryGetValue("dbLocale", out var dbLocale))
        {
            _logger?.LogDebug("GBase Db_locale: {DbLocale}", dbLocale);
            appliedOptions.Add($"dbLocale={dbLocale}");
        }

        if (optimizationSettings.TryGetValue("clientLocale", out var clientLocale))
        {
            _logger?.LogDebug("GBase Client_locale: {ClientLocale}", clientLocale);
            appliedOptions.Add($"clientLocale={clientLocale}");
        }

        // 连接池大小提示
        if (optimizationSettings.TryGetValue("maxPoolSize", out var maxPoolSizeStr) &&
            int.TryParse(maxPoolSizeStr, out var maxPoolSize))
        {
            _logger?.LogDebug("GBase 最大连接池: {MaxPoolSize}", maxPoolSize);
            appliedOptions.Add($"maxPoolSize={maxPoolSize}");
        }

        if (appliedOptions.Count > 0)
        {
            _logger?.LogDebug("GBase 优化配置选项: {Options}", string.Join(", ", appliedOptions));
        }

        _logger?.LogDebug("应用 GBase 性能优化配置");
    }

    public string GetDescription()
    {
        return "GBase 优化：ODBC 兼容 + 分页批量写入 + Locale 配置提示 + 连接池提示";
    }
}