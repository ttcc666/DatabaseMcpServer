using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies.DBSetting;

/// <summary>
/// 华为 GaussDB/OpenGauss 数据库性能优化策略
/// </summary>
public class GaussDbOptimizationStrategy : IDatabaseOptimizationStrategy
{
    private readonly ILogger? _logger;

    public GaussDbOptimizationStrategy(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 应用 GaussDB/OpenGauss 优化配置（驱动模式、Schema、类型映射、批量等）。
    /// </summary>
    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        // GaussDB/OpenGauss 默认：禁用 nvarchar，推荐 Npgsql + NoResetOnClose
        settings.DisableNvarchar = true;

        if (optimizationSettings == null)
        {
            _logger?.LogDebug("应用 GaussDB/OpenGauss 默认配置（禁用 Nvarchar，建议 No Reset On Close=true）");
            return;
        }

        var appliedOptions = new List<string>();

        // 2. 检查是否使用原生驱动模式
        if (optimizationSettings.TryGetValue("nativeDriver", out var nativeDriverStr) &&
            bool.TryParse(nativeDriverStr, out var nativeDriver))
        {
            if (nativeDriver)
            {
                settings.DatabaseModel = DbType.GaussDBNative;
                appliedOptions.Add("nativeDriver=true");
                _logger?.LogDebug("GaussDB 使用原生驱动模式");
            }
            else
            {
                settings.DatabaseModel = DbType.PostgreSQL;
                appliedOptions.Add("nativeDriver=false");
                _logger?.LogDebug("GaussDB 使用 Npgsql 兼容模式");
            }
        }

        // 3. OpenGauss 特定配置
        if (optimizationSettings.TryGetValue("isOpenGauss", out var isOpenGaussStr) &&
            bool.TryParse(isOpenGaussStr, out var isOpenGauss))
        {
            if (isOpenGauss)
            {
                settings.DatabaseModel = DbType.OpenGauss;
                appliedOptions.Add("isOpenGauss=true");
                _logger?.LogDebug("使用 OpenGauss 数据库模式");
            }
        }

        // 4. Schema 支持
        if (optimizationSettings.TryGetValue("schema", out var schemaName))
        {
            appliedOptions.Add($"schema={schemaName}");
            _logger?.LogDebug("GaussDB 使用 Schema: {Schema}", schemaName);
        }

        // 5. 连接池配置
        if (optimizationSettings.TryGetValue("maxPoolSize", out var maxPoolSizeStr) &&
            int.TryParse(maxPoolSizeStr, out var maxPoolSize))
        {
            appliedOptions.Add($"maxPoolSize={maxPoolSize}");
            _logger?.LogDebug("GaussDB 连接池大小: {PoolSize}", maxPoolSize);
        }

        // 5.1 Npgsql 连接建议 No Reset On Close=true
        if (optimizationSettings.TryGetValue("noResetOnClose", out var noResetStr) &&
            bool.TryParse(noResetStr, out var noResetOnClose))
        {
            if (!noResetOnClose)
            {
                _logger?.LogWarning("GaussDB 建议在连接字符串加上 No Reset On Close=true 以避免会话重置问题");
            }
            appliedOptions.Add($"noResetOnClose={noResetOnClose}");
        }
        else
        {
            _logger?.LogInformation("GaussDB 未显式配置 No Reset On Close，推荐在连接字符串中设置为 true");
        }

        // 6. 数据类型映射优化
        if (optimizationSettings.TryGetValue("typeMapping", out var typeMappingStr) &&
            bool.TryParse(typeMappingStr, out var typeMapping))
        {
            if (typeMapping)
            {
                appliedOptions.Add("typeMapping=true");
                _logger?.LogInformation("GaussDB 数据类型映射优化已启用：支持 JSON、Geometry 等特殊类型");
            }
        }

        // 7. 批量操作优化
        if (optimizationSettings.TryGetValue("batchSize", out var batchSizeStr) &&
            int.TryParse(batchSizeStr, out var batchSize))
        {
            appliedOptions.Add($"batchSize={batchSize}");
            _logger?.LogDebug("GaussDB 批量操作大小: {BatchSize}", batchSize);
        }

        if (appliedOptions.Count > 0)
        {
            _logger?.LogDebug("GaussDB/OpenGauss 优化配置选项: {Options}", string.Join(", ", appliedOptions));
        }

        _logger?.LogDebug("应用 GaussDB/OpenGauss 性能优化配置");
    }

    /// <summary>
    /// 获取 GaussDB/OpenGauss 优化策略描述。
    /// </summary>
    public string GetDescription()
    {
        return "GaussDB/OpenGauss 优化：原生驱动支持 + Schema 管理 + 数据类型映射 + 批量操作优化";
    }
}
