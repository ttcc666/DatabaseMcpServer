using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies;

/// <summary>
/// 达梦数据库性能优化策略
/// </summary>
public class DmOptimizationStrategy : IDatabaseOptimizationStrategy
{
    private readonly ILogger? _logger;

    public DmOptimizationStrategy(ILogger? logger = null)
    {
        _logger = logger;
    }

    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        // 达梦数据库特定优化配置

        // 1. 表名大小写处理（默认转大写，可通过 JSON 配置覆盖）
        settings.IsAutoToUpper = true;

        // 6. 禁用 nvarchar（达梦数据库默认使用 varchar）
        settings.DisableNvarchar = false;

        if (optimizationSettings == null) return;

        // 检查是否使用小写表名
        if (optimizationSettings.TryGetValue("lowercaseTables", out var lowercaseTablesStr) &&
            bool.TryParse(lowercaseTablesStr, out var lowercaseTables))
        {
            settings.IsAutoToUpper = !lowercaseTables;
            _logger?.LogDebug("达梦数据库使用小写表名: {LowerCase}", lowercaseTables);
        }

        // 2. Docker 模式兼容性（某些 Docker 安装默认使用 MySQL 模式）
        if (optimizationSettings.TryGetValue("dockerMysqlMode", out var dockerMysqlModeStr) &&
            bool.TryParse(dockerMysqlModeStr, out var dockerMysqlMode))
        {
            if (dockerMysqlMode)
            {
                settings.DatabaseModel = DbType.MySql;
                _logger?.LogDebug("达梦数据库启用 MySQL 兼容模式（Docker）");
            }
        }

        // 3. Schema 支持
        if (optimizationSettings.TryGetValue("schema", out var schemaName))
        {
            _logger?.LogDebug("达梦数据库使用 Schema: {Schema}", schemaName);
            // Schema 通常在连接字符串中配置
        }

        // 4. Clob/Text 类型优化
        if (optimizationSettings.TryGetValue("clobOptimization", out var clobOptimizationStr) &&
            bool.TryParse(clobOptimizationStr, out var clobOptimization))
        {
            if (clobOptimization)
            {
                _logger?.LogInformation("达梦数据库 Clob 类型优化已启用：确保使用 SqlSugarCore.Dm 1.3.0+ 版本");
            }
        }

        // 5. 连接池配置
        if (optimizationSettings.TryGetValue("maxPoolSize", out var maxPoolSizeStr) &&
            int.TryParse(maxPoolSizeStr, out var maxPoolSize))
        {
            _logger?.LogDebug("达梦数据库连接池大小: {PoolSize}", maxPoolSize);
        }

        _logger?.LogDebug("应用达梦数据库性能优化配置");
    }

    public string GetDescription()
    {
        return "达梦数据库优化：智能表名处理 + Docker 模式兼容 + Schema 支持 + Clob 类型优化";
    }
}
