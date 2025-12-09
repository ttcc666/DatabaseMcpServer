using DatabaseMcpServer.Models;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies;

/// <summary>
/// SQL Server 文档生成策略
/// </summary>
public class SqlServerDocumentationStrategy : DatabaseDocumentationStrategyBase
{
    public SqlServerDocumentationStrategy(ILogger? logger = null)
        : base(logger)
    {
    }

    /// <summary>
    /// 获取 SQL Server 外键信息（基于 sys.* 视图）。
    /// </summary>
    public override IEnumerable<ForeignKeyDocumentation> GetForeignKeys(ISqlSugarClient db, string tableName, List<string> warnings)
    {
        const string sql = @"
SELECT
    fk.name AS ConstraintName,
    tp.name AS TableName,
    cp.name AS ColumnName,
    tr.name AS ReferencedTable,
    cr.name AS ReferencedColumn,
    fk.update_referential_action_desc AS UpdateRule,
    fk.delete_referential_action_desc AS DeleteRule
FROM sys.foreign_keys fk
INNER JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
INNER JOIN sys.tables tp ON fkc.parent_object_id = tp.object_id
INNER JOIN sys.columns cp ON fkc.parent_object_id = cp.object_id AND fkc.parent_column_id = cp.column_id
INNER JOIN sys.tables tr ON fkc.referenced_object_id = tr.object_id
INNER JOIN sys.columns cr ON fkc.referenced_object_id = cr.object_id AND fkc.referenced_column_id = cr.column_id
WHERE tp.name = @TableName
  AND SCHEMA_NAME(tp.schema_id) = SCHEMA_NAME();";

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
    /// 获取 SQL Server 表统计信息（行数/数据/索引/总大小）。
    /// </summary>
    public override TableStatistics? GetTableStatistics(ISqlSugarClient db, string tableName, List<string> warnings)
    {
        const string sql = @"
DECLARE @objId INT = OBJECT_ID(QUOTENAME(SCHEMA_NAME()) + '.' + QUOTENAME(@TableName));
SELECT
    SUM(ps.row_count) AS [RowCount],
    SUM(ps.reserved_page_count) AS [ReservedPages],
    SUM(ps.used_page_count) AS [UsedPages],
    SUM(ps.in_row_used_page_count + ps.lob_used_page_count + ps.row_overflow_used_page_count) AS [DataPages]
FROM sys.dm_db_partition_stats ps
WHERE ps.index_id >= 0
  AND (@objId IS NOT NULL AND ps.object_id = @objId);";

        try
        {
            var row = db.Ado.SqlQuerySingle<SqlServerTableStatRow>(sql, new { TableName = tableName });
            if (row == null)
            {
                return null;
            }

            var dataBytes = (row.DataPages ?? 0) * 8L * 1024L;
            var usedBytes = (row.UsedPages ?? 0) * 8L * 1024L;
            var totalBytes = (row.ReservedPages ?? 0) * 8L * 1024L;
            var indexBytes = Math.Max(0, totalBytes - dataBytes);

            return new TableStatistics
            {
                RowCount = row.RowCount,
                DataBytes = dataBytes,
                IndexBytes = indexBytes,
                TotalBytes = totalBytes
            };
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "获取表 {TableName} 的统计信息失败", tableName);
            AddWarningOnce(warnings, $"未能获取表 {tableName} 的统计信息: {ex.Message}");
            return null;
        }
    }

    private sealed class SqlServerTableStatRow
    {
        public long? RowCount { get; set; }
        public long? ReservedPages { get; set; }
        public long? UsedPages { get; set; }
        public long? DataPages { get; set; }
    }
}