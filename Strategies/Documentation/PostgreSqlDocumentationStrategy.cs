using DatabaseMcpServer.Models;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies;

/// <summary>
/// PostgreSQL 文档生成策略
/// </summary>
public class PostgreSqlDocumentationStrategy : DatabaseDocumentationStrategyBase
{
    public PostgreSqlDocumentationStrategy(ILogger? logger = null)
        : base(logger)
    {
    }

    /// <summary>
    /// 获取 PostgreSQL 外键信息（基于 pg_catalog）。
    /// </summary>
    public override IEnumerable<ForeignKeyDocumentation> GetForeignKeys(ISqlSugarClient db, string tableName, List<string> warnings)
    {
        const string sql = @"
SELECT
    con.conname AS ConstraintName,
    att2.attname AS ColumnName,
    rel_rf.relname AS ReferencedTable,
    att_rf.attname AS ReferencedColumn,
    CASE con.confupdtype
        WHEN 'c' THEN 'CASCADE'
        WHEN 'n' THEN 'SET NULL'
        WHEN 'd' THEN 'SET DEFAULT'
        WHEN 'r' THEN 'RESTRICT'
        ELSE 'NO ACTION'
    END AS UpdateRule,
    CASE con.confdeltype
        WHEN 'c' THEN 'CASCADE'
        WHEN 'n' THEN 'SET NULL'
        WHEN 'd' THEN 'SET DEFAULT'
        WHEN 'r' THEN 'RESTRICT'
        ELSE 'NO ACTION'
    END AS DeleteRule
FROM pg_constraint con
INNER JOIN pg_class rel ON rel.oid = con.conrelid
INNER JOIN pg_namespace n ON n.oid = con.connamespace
INNER JOIN pg_class rel_rf ON rel_rf.oid = con.confrelid
INNER JOIN unnest(con.conkey) WITH ORDINALITY AS cols(attnum, ord) ON true
INNER JOIN pg_attribute att2 ON att2.attrelid = con.conrelid AND att2.attnum = cols.attnum
INNER JOIN unnest(con.confkey) WITH ORDINALITY AS cols_rf(attnum, ord) ON cols_rf.ord = cols.ord
INNER JOIN pg_attribute att_rf ON att_rf.attrelid = con.confrelid AND att_rf.attnum = cols_rf.attnum
WHERE con.contype = 'f'
  AND rel.relname = @TableName
  AND n.nspname = current_schema();";

        try
        {
            var rows = db.Ado.SqlQuery<ForeignKeyRow>(sql, new { TableName = tableName });
            return GroupForeignKeys(rows);
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "获取表 {TableName} 的外键失败", tableName);
            AddWarningOnce(warnings, $"未能获取表 {tableName} 的外键: {ex.Message}");
            return Enumerable.Empty<ForeignKeyDocumentation>();
        }
    }

    /// <summary>
    /// 获取 PostgreSQL 表统计信息（行数/数据/索引/总大小）。
    /// </summary>
    public override TableStatistics? GetTableStatistics(ISqlSugarClient db, string tableName, List<string> warnings)
    {
        const string sql = @"
SELECT
    rel.reltuples::bigint AS RowCount,
    pg_relation_size(rel.oid) AS DataBytes,
    (pg_total_relation_size(rel.oid) - pg_relation_size(rel.oid)) AS IndexBytes,
    pg_total_relation_size(rel.oid) AS TotalBytes
FROM pg_class rel
INNER JOIN pg_namespace n ON n.oid = rel.relnamespace
WHERE rel.relname = @TableName
  AND n.nspname = current_schema()
  AND rel.relkind = 'r'
LIMIT 1;";

        try
        {
            return db.Ado.SqlQuerySingle<TableStatistics>(sql, new { TableName = tableName });
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "获取表 {TableName} 的统计信息失败", tableName);
            AddWarningOnce(warnings, $"未能获取表 {tableName} 的统计信息: {ex.Message}");
            return null;
        }
    }
}