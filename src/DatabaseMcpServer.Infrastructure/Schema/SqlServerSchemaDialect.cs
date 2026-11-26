using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;
using SqlSugar;

namespace DatabaseMcpServer.Schema;

internal static class SqlServerSchemaDialect
{
    public static bool SetDefaultValue(ISqlSugarClient db, string tableName, string columnName, string defaultValue)
    {
        var (schemaName, pureTableName) = ParseObjectName(tableName);
        ValidateColumn(columnName);
        var ownsTransaction = db.Ado.Transaction == null;
        if (ownsTransaction) db.Ado.BeginTran();
        try
        {
            var existingConstraintName = db.Ado.GetString("""
                SELECT dc.name FROM sys.default_constraints dc
                JOIN sys.columns c ON c.default_object_id=dc.object_id
                JOIN sys.tables t ON t.object_id=c.object_id
                JOIN sys.schemas s ON s.schema_id=t.schema_id
                WHERE s.name=@SchemaName AND t.name=@TableName AND c.name=@ColumnName
                """, new SugarParameter("@SchemaName", schemaName), new SugarParameter("@TableName", pureTableName),
                new SugarParameter("@ColumnName", columnName));
            if (!string.IsNullOrWhiteSpace(existingConstraintName))
                db.Ado.ExecuteCommand($"ALTER TABLE {QuoteSqlServerTableName(schemaName, pureTableName)} DROP CONSTRAINT {QuoteSqlServerIdentifier(existingConstraintName)}");
            var constraintName = BuildSqlServerDefaultConstraintName(pureTableName, columnName);
            db.Ado.ExecuteCommand($"ALTER TABLE {QuoteSqlServerTableName(schemaName, pureTableName)} ADD CONSTRAINT {QuoteSqlServerIdentifier(constraintName)} DEFAULT {defaultValue} FOR {QuoteSqlServerIdentifier(columnName)}");
            if (ownsTransaction) db.Ado.CommitTran();
            return true;
        }
        catch
        {
            if (ownsTransaction) db.Ado.RollbackTran();
            throw;
        }
    }

    public static bool SetColumnRemark(ISqlSugarClient db, string tableName, string columnName, string description)
        => SetRemark(db, tableName, columnName, description);

    public static bool DeleteColumnRemark(ISqlSugarClient db, string tableName, string columnName)
        => ChangeRemark(db, "sp_dropextendedproperty", tableName, columnName, null);

    public static bool SetTableRemark(ISqlSugarClient db, string tableName, string description)
        => SetRemark(db, tableName, null, description);

    public static bool DeleteTableRemark(ISqlSugarClient db, string tableName)
        => ChangeRemark(db, "sp_dropextendedproperty", tableName, null, null);

    public static bool HasTableRemark(ISqlSugarClient db, string tableName) => HasRemark(db, tableName, null);

    private static bool SetRemark(ISqlSugarClient db, string tableName, string? columnName, string description)
        => ChangeRemark(db, HasRemark(db, tableName, columnName) ? "sp_updateextendedproperty" : "sp_addextendedproperty",
            tableName, columnName, description);

    private static bool HasRemark(ISqlSugarClient db, string tableName, string? columnName)
    {
        var (schema, table) = ParseObjectName(tableName);
        ValidateColumn(columnName);
        return db.Ado.GetInt("""
            SELECT COUNT(*) FROM sys.extended_properties ep
            JOIN sys.tables t ON t.object_id=ep.major_id
            JOIN sys.schemas s ON s.schema_id=t.schema_id
            LEFT JOIN sys.columns c ON c.object_id=t.object_id AND c.column_id=ep.minor_id
            WHERE ep.class=1 AND ep.name=N'MS_Description' AND s.name=@schema AND t.name=@table
                AND ((@column IS NULL AND ep.minor_id=0) OR c.name=@column)
            """, new SugarParameter("@schema", schema), new SugarParameter("@table", table),
            new SugarParameter("@column", columnName) { DbType = System.Data.DbType.String }) > 0;
    }

    private static bool ChangeRemark(ISqlSugarClient db, string procedure, string tableName, string? columnName, string? description)
    {
        var (schema, table) = ParseObjectName(tableName);
        ValidateColumn(columnName);
        var parameters = new List<SugarParameter> { new("@schema", schema), new("@table", table) };
        var sql = $"EXEC sys.{procedure} @name=N'MS_Description',@level0type=N'SCHEMA',@level0name=@schema,@level1type=N'TABLE',@level1name=@table";
        if (columnName != null)
        {
            sql += ",@level2type=N'COLUMN',@level2name=@column";
            parameters.Add(new("@column", columnName));
        }
        if (procedure != "sp_dropextendedproperty")
        {
            sql += ",@value=@description";
            parameters.Add(new("@description", description));
        }
        db.Ado.ExecuteCommand(sql, parameters.ToArray());
        return true;
    }

    internal static (string SchemaName, string TableName) ParseObjectName(string tableName)
    {
        SqlSafetyGuard.EnsureSafeTableName(tableName);
        var parts = tableName.Split('.');
        if (parts.Length is < 1 or > 2 || parts.Any(p => p.Length is < 1 or > 128))
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "tableName 仅支持 table 或 schema.table，每段为 1-128 个字符。");
        return parts.Length == 1 ? ("dbo", parts[0]) : (parts[0], parts[1]);
    }

    private static void ValidateColumn(string? columnName)
    {
        if (columnName == null) return;
        SqlSafetyGuard.EnsureSafeTableName(columnName);
        if (columnName.Contains('.') || columnName.Length > 128)
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "columnName 必须是单个 1-128 字符标识符。");
    }

    private static string QuoteSqlServerTableName(string schemaName, string tableName)
    {
        return $"{QuoteSqlServerIdentifier(schemaName)}.{QuoteSqlServerIdentifier(tableName)}";
    }

    private static string QuoteSqlServerIdentifier(string identifier)
    {
        return $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
    }

    private static string BuildSqlServerDefaultConstraintName(string tableName, string columnName)
    {
        var sanitized = $"{tableName}_{columnName}"
            .Replace(".", "_", StringComparison.Ordinal)
            .Replace("-", "_", StringComparison.Ordinal);

        var prefix = sanitized.Length > 96 ? sanitized[..96] : sanitized;
        var candidate = $"DF_{prefix}_{Guid.NewGuid():N}";
        return candidate.Length > 128 ? candidate[..128] : candidate;
    }
}
