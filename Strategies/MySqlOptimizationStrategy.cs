using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies;

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

        _logger?.LogDebug("应用 MySQL 性能优化配置");
    }

    public string GetDescription()
    {
        return "MySQL 性能优化：utf8mb4 字符集支持 + 连接池配置";
    }
}
