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

    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        // 启用 NoLock 提高并发读取性能
        settings.IsWithNoLockQuery = true;

        // 事务中禁用 NoLock
        settings.DisableWithNoLockWithTran = true;

        // 默认不禁用 nvarchar，可通过 JSON 配置覆盖
        settings.DisableNvarchar = false;

        if (optimizationSettings == null) return;

        // 检查是否需要禁用 nvarchar（性能优化）
        if (optimizationSettings.TryGetValue("disableNvarchar", out var disableNvarcharStr) &&
            bool.TryParse(disableNvarcharStr, out var disableNvarchar))
        {
            settings.DisableNvarchar = disableNvarchar;
            _logger?.LogDebug("SQL Server 禁用 Nvarchar: {Disabled}", disableNvarchar);
        }

        _logger?.LogDebug("应用 SQL Server 性能优化配置（NoLock: {NoLock}）", settings.IsWithNoLockQuery);
    }

    public string GetDescription()
    {
        return "SQL Server 性能优化：自动 NoLock + 连接池 + 可选禁用 nvarchar";
    }
}