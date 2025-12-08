using DatabaseMcpServer.Models;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies;

/// <summary>
/// Oracle 文档生成策略
/// </summary>
public class OracleDocumentationStrategy : DatabaseDocumentationStrategyBase
{
    public OracleDocumentationStrategy(ILogger? logger = null)
        : base(logger)
    {
    }

    public override IEnumerable<ForeignKeyDocumentation> GetForeignKeys(ISqlSugarClient db, string tableName, List<string> warnings)
    {
        const string sql = @"
SELECT
    ac.CONSTRAINT_NAME AS ConstraintName,
    acc.COLUMN_NAME AS ColumnName,
    ac_r.TABLE_NAME AS ReferencedTable,
    acc_r.COLUMN_NAME AS ReferencedColumn,
    'NO ACTION' AS UpdateRule,
    CASE ac.DELETE_RULE
        WHEN 'CASCADE' THEN 'CASCADE'
        WHEN 'SET NULL' THEN 'SET NULL'
        ELSE 'NO ACTION'
    END AS DeleteRule
FROM USER_CONSTRAINTS ac
INNER JOIN USER_CONS_COLUMNS acc ON ac.CONSTRAINT_NAME = acc.CONSTRAINT_NAME
INNER JOIN USER_CONSTRAINTS ac_r ON ac.R_CONSTRAINT_NAME = ac_r.CONSTRAINT_NAME
INNER JOIN USER_CONS_COLUMNS acc_r
    ON ac_r.CONSTRAINT_NAME = acc_r.CONSTRAINT_NAME
   AND acc_r.POSITION = acc.POSITION
WHERE ac.CONSTRAINT_TYPE = 'R'
  AND ac.TABLE_NAME = UPPER(:TableName);";

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
WITH index_bytes AS (
    SELECT NVL(SUM(us.BYTES), 0) AS Bytes
    FROM USER_SEGMENTS us
    WHERE us.SEGMENT_TYPE LIKE 'INDEX%'
      AND us.SEGMENT_NAME IN (SELECT index_name FROM USER_INDEXES WHERE table_name = UPPER(:TableName))
),
table_bytes AS (
    SELECT NVL(SUM(us.BYTES), 0) AS Bytes
    FROM USER_SEGMENTS us
    WHERE us.SEGMENT_TYPE LIKE 'TABLE%'
      AND us.SEGMENT_NAME = UPPER(:TableName)
)
SELECT
    ut.NUM_ROWS AS RowCount,
    tb.Bytes AS DataBytes,
    ib.Bytes AS IndexBytes,
    (tb.Bytes + ib.Bytes) AS TotalBytes
FROM USER_TABLES ut
CROSS JOIN table_bytes tb
CROSS JOIN index_bytes ib
WHERE ut.TABLE_NAME = UPPER(:TableName);";

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

    public override string? GetTableDdl(ISqlSugarClient db, string tableName, List<string> warnings)
    {
        const string sql = "SELECT DBMS_METADATA.GET_DDL('TABLE', UPPER(:TableName)) AS Ddl FROM dual";

        try
        {
            return db.Ado.SqlQuerySingle<string>(sql, new { TableName = tableName });
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "获取表 {TableName} 的 DDL 摘要失败", tableName);
            AddWarningOnce(warnings, $"未能获取表 {tableName} 的 DDL 摘要: {ex.Message}");
            return null;
        }
    }

    public override IEnumerable<string> GetTriggers(ISqlSugarClient db, string tableName, List<string> warnings)
    {
        const string sql = "SELECT trigger_name FROM user_triggers WHERE table_name = UPPER(:TableName)";

        try
        {
            var triggers = db.Ado.SqlQuery<string>(sql, new { TableName = tableName }) ?? new List<string>();
            return triggers.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim());
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "获取表 {TableName} 的触发器失败", tableName);
            AddWarningOnce(warnings, $"未能获取表 {tableName} 的触发器: {ex.Message}");
            return Enumerable.Empty<string>();
        }
    }
}
