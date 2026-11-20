using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies.DBSetting;

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

    /// <summary>
    /// 应用达梦数据库优化配置（表名大小写、Docker/MySQL 模式、Schema、Clob 等）。
    /// </summary>
    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        // 默认配置：表名转大写、不禁用 nvarchar
        settings.IsAutoToUpper = true;
        settings.DisableNvarchar = false;

        if (optimizationSettings == null)
        {
            _logger?.LogDebug("应用达梦默认性能优化配置（表名转大写，Nvarchar 启用）");
            return;
        }

        var appliedOptions = new List<string>();

        // 检查是否使用小写表名
        if (optimizationSettings.TryGetValue("lowercaseTables", out var lowercaseTablesStr) &&
            bool.TryParse(lowercaseTablesStr, out var lowercaseTables))
        {
            settings.IsAutoToUpper = !lowercaseTables;
            appliedOptions.Add($"lowercaseTables={lowercaseTables}");
            _logger?.LogDebug("达梦数据库使用小写表名: {LowerCase}", lowercaseTables);
        }

        // 2. Docker 模式兼容性（某些 Docker 安装默认使用 MySQL 模式）
        if (optimizationSettings.TryGetValue("dockerMysqlMode", out var dockerMysqlModeStr) &&
            bool.TryParse(dockerMysqlModeStr, out var dockerMysqlMode))
        {
            if (dockerMysqlMode)
            {
                settings.DatabaseModel = DbType.MySql;
                appliedOptions.Add("dockerMysqlMode=true");
                _logger?.LogDebug("达梦数据库启用 MySQL 兼容模式（Docker）");
            }
        }

        // 3. Schema 支持
        if (optimizationSettings.TryGetValue("schema", out var schemaName))
        {
            appliedOptions.Add($"schema={schemaName}");
            _logger?.LogDebug("达梦数据库使用 Schema: {Schema}", schemaName);
            // Schema 通常在连接字符串中配置
        }

        // 4. Clob/Text 类型优化
        if (optimizationSettings.TryGetValue("clobOptimization", out var clobOptimizationStr) &&
            bool.TryParse(clobOptimizationStr, out var clobOptimization))
        {
            if (clobOptimization)
            {
                appliedOptions.Add("clobOptimization=true");
                _logger?.LogInformation("达梦数据库 Clob 类型优化已启用：确保使用 SqlSugarCore.Dm 1.3.0+ 版本");
            }
        }

        // 5. 连接池配置
        if (optimizationSettings.TryGetValue("maxPoolSize", out var maxPoolSizeStr) &&
            int.TryParse(maxPoolSizeStr, out var maxPoolSize))
        {
            appliedOptions.Add($"maxPoolSize={maxPoolSize}");
            _logger?.LogDebug("达梦数据库连接池大小: {PoolSize}", maxPoolSize);
        }

        if (appliedOptions.Count > 0)
        {
            _logger?.LogDebug("达梦优化配置选项: {Options}", string.Join(", ", appliedOptions));
        }

        _logger?.LogDebug("应用达梦数据库性能优化配置");
    }

    /// <summary>
    /// 获取达梦数据库优化策略描述。
    /// </summary>
    public string GetDescription()
    {
        return "达梦数据库优化：智能表名处理 + Docker 模式兼容 + Schema 支持 + Clob 类型优化";
    }
}
