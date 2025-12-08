using DatabaseMcpServer.Models;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies;

/// <summary>
/// MySQL 文档生成策略（兼容 MySqlConnector）
/// </summary>
public class MySqlDocumentationStrategy : DatabaseDocumentationStrategyBase
{
    public MySqlDocumentationStrategy(ILogger? logger = null)
        : base(logger)
    {
    }

    public override IEnumerable<ForeignKeyDocumentation> GetForeignKeys(ISqlSugarClient db, string tableName, List<string> warnings)
    {
        const string sql = @"
SELECT
    k.CONSTRAINT_NAME AS ConstraintName,
    k.COLUMN_NAME AS ColumnName,
    k.REFERENCED_TABLE_NAME AS ReferencedTable,
    k.REFERENCED_COLUMN_NAME AS ReferencedColumn,
    rc.UPDATE_RULE AS UpdateRule,
    rc.DELETE_RULE AS DeleteRule
FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE k
INNER JOIN INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS rc
    ON k.CONSTRAINT_NAME = rc.CONSTRAINT_NAME
   AND k.TABLE_SCHEMA = rc.CONSTRAINT_SCHEMA
WHERE k.TABLE_SCHEMA = DATABASE()
  AND k.TABLE_NAME = @TableName
  AND k.REFERENCED_TABLE_NAME IS NOT NULL;";

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

    public override TableStatistics? GetTableStatistics(ISqlSugarClient db, string tableName, List<string> warnings)
    {
        const string sql = @"
SELECT
    CAST(TABLE_ROWS AS SIGNED) AS RowCount,
    CAST(DATA_LENGTH AS SIGNED) AS DataBytes,
    CAST(INDEX_LENGTH AS SIGNED) AS IndexBytes,
    CAST(DATA_LENGTH + INDEX_LENGTH AS SIGNED) AS TotalBytes
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_NAME = @TableName
LIMIT 1;";

        try
        {
            var row = db.Ado.SqlQuerySingle<MySqlTableStatRow>(sql, new { TableName = tableName });
            if (row == null)
            {
                return null;
            }

            return new TableStatistics
            {
                RowCount = row.RowCount,
                DataBytes = row.DataBytes,
                IndexBytes = row.IndexBytes,
                TotalBytes = row.TotalBytes
            };
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "获取表 {TableName} 的统计信息失败", tableName);
            AddWarningOnce(warnings, $"未能获取表 {tableName} 的统计信息: {ex.Message}");
            return null;
        }
    }

    public override string? GetTableDdl(ISqlSugarClient db, string tableName, List<string> warnings)
    {
        var safeName = tableName.Replace("`", "``");
        var sql = $"SHOW CREATE TABLE `{safeName}`;";

        try
        {
            var row = db.Ado.SqlQuerySingle<MySqlShowCreateTableRow>(sql);
            return row?.CreateTable;
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "获取表 {TableName} 的 DDL 摘要失败", tableName);
            AddWarningOnce(warnings, $"未能获取表 {tableName} 的 DDL 摘要: {ex.Message}");
            return null;
        }
    }

    private sealed class MySqlTableStatRow
    {
        public long? RowCount { get; set; }
        public long? DataBytes { get; set; }
        public long? IndexBytes { get; set; }
        public long? TotalBytes { get; set; }
    }

    private sealed class MySqlShowCreateTableRow
    {
        [SugarColumn(ColumnName = "Create Table")]
        public string? CreateTable { get; set; }
    }
}
