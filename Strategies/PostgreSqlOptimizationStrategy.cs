using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies;

/// <summary>
/// PostgreSQL 性能优化策略
/// 参考文档: Doc/donet5_postgresql.md
/// </summary>
public class PostgreSqlOptimizationStrategy : IDatabaseOptimizationStrategy
{
    private readonly ILogger? _logger;

    public PostgreSqlOptimizationStrategy(ILogger? logger = null)
    {
        _logger = logger;
    }

    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        // PostgreSQL 表名自动转小写（默认启用，推荐）
        if (optimizationSettings != null &&
            optimizationSettings.TryGetValue("autoToLower", out var autoToLowerStr) &&
            bool.TryParse(autoToLowerStr, out var autoToLower))
        {
            settings.PgSqlIsAutoToLower = autoToLower;
            _logger?.LogDebug("PostgreSQL 表名自动转小写: {Enabled}", autoToLower);
        }
        else
        {
            // 默认启用（推荐规范）
            settings.PgSqlIsAutoToLower = true;
        }

        if (optimizationSettings == null) return;

        // PostgreSQL ILike 不区分大小写查询
        if (optimizationSettings.TryGetValue("enableILike", out var enableILikeStr) &&
            bool.TryParse(enableILikeStr, out var enableILike))
        {
            settings.EnableILike = enableILike;
            _logger?.LogDebug("PostgreSQL 启用 ILike: {Enabled}", enableILike);
        }

        // PostgreSQL 自增策略（Serial 或 Identity）
        if (optimizationSettings.TryGetValue("identityStrategy", out var identityStrategy))
        {
            if (identityStrategy.Equals("Identity", StringComparison.OrdinalIgnoreCase))
            {
                settings.PostgresIdentityStrategy = PostgresIdentityStrategy.Identity;
                _logger?.LogDebug("PostgreSQL 使用 Identity 自增策略（高版本）");
            }
            else if (identityStrategy.Equals("Serial", StringComparison.OrdinalIgnoreCase))
            {
                settings.PostgresIdentityStrategy = PostgresIdentityStrategy.Serial;
                _logger?.LogDebug("PostgreSQL 使用 Serial 自增策略（兼容低版本）");
            }
        }
        else
        {
            // 默认使用 Serial（兼容低版本）
            settings.PostgresIdentityStrategy = PostgresIdentityStrategy.Serial;
        }

        _logger?.LogDebug("应用 PostgreSQL 性能优化配置");
    }

    public string GetDescription()
    {
        return "PostgreSQL 性能优化：表名规范 + ILike 支持 + 自增策略 + JSON/数组类型";
    }
}