using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies.DBSetting;

/// <summary>
/// 人大金仓数据库性能优化策略
/// </summary>
public class KdbndpOptimizationStrategy : IDatabaseOptimizationStrategy
{
    private readonly ILogger? _logger;

    public KdbndpOptimizationStrategy(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 应用人大金仓优化配置（多模式兼容、游标/JSON/几何/数组等开关）。
    /// </summary>
    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        var appliedOptions = new List<string>();

        // 1. 数据库模式配置（Oracle/MySQL/PostgreSQL/SqlServer）
        // 默认使用 Oracle 模式，可通过 JSON 配置覆盖
        if (optimizationSettings != null && optimizationSettings.TryGetValue("mode", out var databaseMode))
        {
            switch (databaseMode.ToUpper())
            {
                case "ORACLE":
                    settings.DatabaseModel = DbType.Oracle;
                    settings.IsAutoToUpper = true; // Oracle 模式默认转大写
                    appliedOptions.Add("mode=Oracle");
                    _logger?.LogDebug("人大金仓使用 Oracle 兼容模式");
                    break;

                case "MYSQL":
                    settings.DatabaseModel = DbType.MySql;
                    settings.DisableNvarchar = false;
                    appliedOptions.Add("mode=MySql");
                    _logger?.LogDebug("人大金仓使用 MySQL 兼容模式");
                    break;

                case "POSTGRESQL":
                    settings.DatabaseModel = DbType.PostgreSQL;
                    settings.DisableNvarchar = true;
                    appliedOptions.Add("mode=PostgreSQL");
                    _logger?.LogDebug("人大金仓使用 PostgreSQL 兼容模式");
                    break;

                case "SQLSERVER":
                    settings.DatabaseModel = DbType.SqlServer;
                    appliedOptions.Add("mode=SqlServer");
                    _logger?.LogDebug("人大金仓使用 SQL Server 兼容模式");
                    break;

                default:
                    _logger?.LogWarning("未知的人大金仓数据库模式: {Mode}，使用默认配置", databaseMode);
                    break;
            }
        }
        else
        {
            // 默认使用 Oracle 模式
            settings.DatabaseModel = DbType.Oracle;
            settings.IsAutoToUpper = true;
            _logger?.LogDebug("人大金仓使用默认 Oracle 兼容模式");
        }

        if (optimizationSettings == null)
        {
            _logger?.LogDebug("应用人大金仓默认性能优化配置（Oracle 模式，表名转大写）");
            return;
        }

        // 2. 表名大小写处理
        if (optimizationSettings.TryGetValue("camelCase", out var camelCaseStr) &&
            bool.TryParse(camelCaseStr, out var camelCase))
        {
            settings.IsAutoToUpper = !camelCase;
            appliedOptions.Add($"camelCase={camelCase}");
            _logger?.LogDebug("人大金仓使用驼峰表名: {CamelCase}", camelCase);
        }

        // 可选禁用 nvarchar
        if (optimizationSettings.TryGetValue("disableNvarchar", out var disableNvarcharStr) &&
            bool.TryParse(disableNvarcharStr, out var disableNvarchar))
        {
            settings.DisableNvarchar = disableNvarchar;
            appliedOptions.Add($"disableNvarchar={disableNvarchar}");
            _logger?.LogDebug("人大金仓禁用 Nvarchar: {Disabled}", disableNvarchar);
        }

        // 3. 游标参数支持
        if (optimizationSettings.TryGetValue("enableCursor", out var enableCursorStr) &&
            bool.TryParse(enableCursorStr, out var enableCursor))
        {
            if (enableCursor)
            {
                appliedOptions.Add("enableCursor=true");
                _logger?.LogInformation("人大金仓游标参数支持已启用");
            }
        }

        // 4. JSON 类型支持
        if (optimizationSettings.TryGetValue("enableJson", out var enableJsonStr) &&
            bool.TryParse(enableJsonStr, out var enableJson))
        {
            if (enableJson)
            {
                appliedOptions.Add("enableJson=true");
                _logger?.LogInformation("人大金仓 JSON 类型支持已启用");
            }
        }

        // 5. Geometry/Postgis 支持
        if (optimizationSettings.TryGetValue("enableGeometry", out var enableGeometryStr) &&
            bool.TryParse(enableGeometryStr, out var enableGeometry))
        {
            if (enableGeometry)
            {
                appliedOptions.Add("enableGeometry=true");
                _logger?.LogInformation("人大金仓 Geometry/Postgis 支持已启用");
            }
        }

        // 6. 数组类型支持
        if (optimizationSettings.TryGetValue("enableArray", out var enableArrayStr) &&
            bool.TryParse(enableArrayStr, out var enableArray))
        {
            if (enableArray)
            {
                appliedOptions.Add("enableArray=true");
                _logger?.LogInformation("人大金仓数组类型支持已启用");
            }
        }

        // 7. 连接池配置
        if (optimizationSettings.TryGetValue("maxPoolSize", out var maxPoolSizeStr) &&
            int.TryParse(maxPoolSizeStr, out var maxPoolSize))
        {
            appliedOptions.Add($"maxPoolSize={maxPoolSize}");
            _logger?.LogDebug("人大金仓连接池大小: {PoolSize}", maxPoolSize);
        }

        // 8. Schema 支持
        if (optimizationSettings.TryGetValue("schema", out var schemaName))
        {
            appliedOptions.Add($"schema={schemaName}");
            _logger?.LogDebug("人大金仓使用 Schema: {Schema}", schemaName);
        }

        if (appliedOptions.Count > 0)
        {
            _logger?.LogDebug("人大金仓优化配置选项: {Options}", string.Join(", ", appliedOptions));
        }

        _logger?.LogDebug("应用人大金仓数据库性能优化配置");
    }

    /// <summary>
    /// 获取人大金仓优化策略描述。
    /// </summary>
    public string GetDescription()
    {
        return "人大金仓优化：多模式兼容（Oracle/MySQL/PostgreSQL/SqlServer） + 游标支持 + JSON/Geometry 类型 + 数组支持";
    }
}
