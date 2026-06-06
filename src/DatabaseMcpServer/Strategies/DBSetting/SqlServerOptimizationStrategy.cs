using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies.DBSetting;

/// <summary>
/// SQL Server 性能优化策略
/// </summary>
public class SqlServerOptimizationStrategy : IDatabaseOptimizationStrategy
{
    private readonly ILogger? _logger;

    public SqlServerOptimizationStrategy(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 应用 SQL Server 优化配置（NoLock/禁用 Nvarchar 等）。
    /// </summary>
    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        // 默认开启 NoLock，事务中关闭；默认不禁用 nvarchar
        settings.IsWithNoLockQuery = true;
        settings.DisableWithNoLockWithTran = true;
        settings.DisableNvarchar = false;

        if (optimizationSettings == null)
        {
            _logger?.LogDebug("应用 SQL Server 默认性能优化配置（NoLock=默认开启，事务内自动关闭）");
            return;
        }

        var appliedOptions = new List<string>();

        // 可选控制 NoLock 开关
        if (optimizationSettings.TryGetValue("enableNoLock", out var enableNoLockStr) &&
            bool.TryParse(enableNoLockStr, out var enableNoLock))
        {
            settings.IsWithNoLockQuery = enableNoLock;
            appliedOptions.Add($"enableNoLock={enableNoLock}");
            _logger?.LogDebug("SQL Server NoLock 开关: {Enabled}", enableNoLock);
        }

        // 可选控制事务内 NoLock 行为
        if (optimizationSettings.TryGetValue("disableNoLockWithTran", out var disableNoLockWithTranStr) &&
            bool.TryParse(disableNoLockWithTranStr, out var disableNoLockWithTran))
        {
            settings.DisableWithNoLockWithTran = disableNoLockWithTran;
            appliedOptions.Add($"disableNoLockWithTran={disableNoLockWithTran}");
            _logger?.LogDebug("SQL Server 事务内禁用 NoLock: {Disabled}", disableNoLockWithTran);
        }

        // 禁用 nvarchar（部分场景索引优化）
        if (optimizationSettings.TryGetValue("disableNvarchar", out var disableNvarcharStr) &&
            bool.TryParse(disableNvarcharStr, out var disableNvarchar))
        {
            settings.DisableNvarchar = disableNvarchar;
            appliedOptions.Add($"disableNvarchar={disableNvarchar}");
            _logger?.LogDebug("SQL Server 禁用 Nvarchar: {Disabled}", disableNvarchar);
        }

        if (appliedOptions.Count > 0)
        {
            _logger?.LogDebug("SQL Server 优化配置选项: {Options}", string.Join(", ", appliedOptions));
        }

        _logger?.LogDebug("应用 SQL Server 性能优化配置（NoLock: {NoLock}）", settings.IsWithNoLockQuery);
    }

    /// <summary>
    /// 获取 SQL Server 优化策略描述。
    /// </summary>
    public string GetDescription()
    {
        return "SQL Server 性能优化：自动 NoLock + 连接池 + 可选禁用 nvarchar";
    }
}
