using DatabaseMcpServer.Models;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies;

/// <summary>
/// 数据库文档生成策略工厂
/// </summary>
public class DatabaseDocumentationStrategyFactory
{
    private readonly ILogger<DatabaseDocumentationStrategyFactory>? _logger;

    public DatabaseDocumentationStrategyFactory(ILogger<DatabaseDocumentationStrategyFactory>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 根据数据库类型获取文档生成策略。
    /// </summary>
    public IDatabaseDocumentationStrategy GetStrategy(DbType dbType)
    {
        return dbType switch
        {
            DbType.MySql or DbType.MySqlConnector => new MySqlDocumentationStrategy(_logger),
            DbType.PostgreSQL => new PostgreSqlDocumentationStrategy(_logger),
            DbType.SqlServer => new SqlServerDocumentationStrategy(_logger),
            DbType.Oracle => new OracleDocumentationStrategy(_logger),
            _ => CreateDefaultStrategy(dbType)
        };
    }

    /// <summary>
    /// 创建默认文档策略并记录警告。
    /// </summary>
    private IDatabaseDocumentationStrategy CreateDefaultStrategy(DbType dbType)
    {
        _logger?.LogWarning("数据库类型 {DbType} 未提供专用文档策略，使用默认行为", dbType);
        return new DefaultDocumentationStrategy(dbType, _logger);
    }
}

internal class DefaultDocumentationStrategy : DatabaseDocumentationStrategyBase
{
    private readonly string _dbTypeName;

    public DefaultDocumentationStrategy(DbType dbType, ILogger? logger = null)
        : base(logger)
    {
        _dbTypeName = dbType.ToString();
    }

    /// <summary>
    /// 默认不支持外键，记录警告。
    /// </summary>
    public override IEnumerable<ForeignKeyDocumentation> GetForeignKeys(ISqlSugarClient db, string tableName, List<string> warnings)
    {
        AddWarningOnce(warnings, $"数据库类型 {_dbTypeName} 暂不支持外键信息提取，已跳过。");
        return Enumerable.Empty<ForeignKeyDocumentation>();
    }

    /// <summary>
    /// 默认不支持统计信息，记录警告。
    /// </summary>
    public override TableStatistics? GetTableStatistics(ISqlSugarClient db, string tableName, List<string> warnings)
    {
        AddWarningOnce(warnings, $"数据库类型 {_dbTypeName} 暂不支持统计/容量信息提取，已跳过。");
        return null;
    }

    /// <summary>
    /// 默认不支持 DDL 摘要，记录警告。
    /// </summary>
    public override string? GetTableDdl(ISqlSugarClient db, string tableName, List<string> warnings)
    {
        AddWarningOnce(warnings, $"数据库类型 {_dbTypeName} 暂不支持 DDL 摘要提取，已跳过。");
        return null;
    }
}