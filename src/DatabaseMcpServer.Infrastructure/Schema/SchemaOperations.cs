using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using Microsoft.Extensions.Logging;
using SqlSugar;
using System.Text.Json;

namespace DatabaseMcpServer.Operations;

/// <summary>
/// 数据库架构管理工具类，用于执行数据库架构相关操作。
/// </summary>
internal class SchemaOperations : SqlOperationExecutor
{
    private readonly DatabaseMetadataCache _metadataCache;
    public SchemaOperations(
        IDatabaseConfigService databaseConfig,
        IDatabaseHelperService databaseHelper,
        ILogger<SchemaOperations> logger, DatabaseMetadataCache metadataCache, DatabaseExecutionOptions executionOptions, SqlDatabaseSessions sessions)
        : base(databaseConfig, databaseHelper, logger, executionOptions, metadataCache, sessions)
    {
        _metadataCache = metadataCache;
    }

    public object GetDataBaseList(string? databaseName = null) => QueryData(db => db.DbMaintenance.GetDataBaseList(), databaseName);

    public object GetViewInfoList(string? databaseName = null) => QueryData(db => db.DbMaintenance.GetViewInfoList(false), databaseName);

    public object GetTableInfoList(string? databaseName = null) => QueryData(db => db.DbMaintenance.GetTableInfoList(false), databaseName);

    public object GetColumnInfosByTableName(string tableName,
        string? databaseName = null)
        => QueryData(db => db.DbMaintenance.GetColumnInfosByTableName(tableName, false), databaseName);

    public object GetIsIdentities(string tableName,
        string? databaseName = null)
        => QueryData(db => db.DbMaintenance.GetColumnInfosByTableName(tableName, false)
            .Where(column => column.IsIdentity).Select(column => column.DbColumnName).ToList(), databaseName);

    public object GetPrimaries(string tableName,
        string? databaseName = null)
        => QueryData(db => db.DbMaintenance.GetColumnInfosByTableName(tableName, false)
            .Where(column => column.IsPrimarykey).Select(column => column.DbColumnName).ToList(), databaseName);

    public object IsAnyTable(string tableName,
        string? databaseName = null)
        => QueryExists(db => db.DbMaintenance.IsAnyTable(tableName, false), databaseName);

    public object IsAnyColumn(
        string tableName,
        string columnName,
        string? databaseName = null)
        => QueryExists(db => db.DbMaintenance.IsAnyColumn(tableName, columnName, false), databaseName);

    public object IsAnyConstraint(string constraintName,
        string? databaseName = null)
        => QueryExists(db => db.DbMaintenance.IsAnyConstraint(constraintName), databaseName);

    public object CreateTable(
        string tableName,
        string columnsInfo,
        bool isCreatePrimaryKey = true,
        string? databaseName = null)
    {
        return WithClient(db =>
        {
            SqlSafetyGuard.EnsureSafeTableName(tableName);
            var columns = SchemaColumnDefinitionParser.ParseMany(columnsInfo);
            return new { success = db.DbMaintenance.CreateTable(tableName, columns, isCreatePrimaryKey) };
        }, databaseName, DatabaseToolAccess.Schema);
    }

    public object DropTable(string tableName,
        string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.DropTable(tableName), databaseName);

    public object TruncateTable(string tableName,
        string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.TruncateTable(tableName), databaseName);

    public object BackupTable(
        string oldTableName,
        string newTableName,
        string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.BackupTable(oldTableName, newTableName), databaseName);

    public object RenameTable(
        string oldTableName,
        string newTableName,
        string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.RenameTable(oldTableName, newTableName), databaseName);

    public object AddColumn(
        string tableName,
        string columnInfo,
        string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.AddColumn(tableName, SchemaColumnDefinitionParser.Parse(columnInfo)), databaseName);

    public object UpdateColumn(
        string tableName,
        string columnInfo,
        string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.UpdateColumn(tableName, SchemaColumnDefinitionParser.Parse(columnInfo)), databaseName);

    public object DropColumn(
        string tableName,
        string columnName,
        string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.DropColumn(tableName, columnName), databaseName);

    public object RenameColumn(
        string tableName,
        string oldColumnName,
        string newColumnName,
        string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.RenameColumn(tableName, oldColumnName, newColumnName), databaseName);

    public object AddPrimaryKey(
        string tableName,
        string columnName,
        string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.AddPrimaryKey(tableName, columnName), databaseName);

    public object DropConstraint(
        string tableName,
        string constraintName,
        string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.DropConstraint(tableName, constraintName), databaseName);

    public object CreateIndex(
        string tableName,
        string indexName,
        string columnName,
        bool isUnique = false,
        string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.CreateIndex(tableName, [columnName], indexName, isUnique), databaseName);

    public object GetIndexList(string tableName,
        string? databaseName = null)
        => QueryData(db => db.DbMaintenance.GetIndexList(tableName), databaseName);

    public object AddDefaultValue(
        string tableName,
        string columnName,
        string defaultValue,
        string? databaseName = null)
    {
        return WithClient(db =>
        {
            if (string.IsNullOrWhiteSpace(defaultValue))
            {
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "defaultValue 不能为空");
            }

            if (db.CurrentConnectionConfig.DbType != DbType.SqlServer)
            {
                return new { success = db.DbMaintenance.AddDefaultValue(tableName, columnName, defaultValue) };
            }

            var (schemaName, pureTableName) = ParseSqlServerObjectName(tableName);
            var existingConstraintSql = @"
SELECT dc.name
FROM sys.default_constraints dc
INNER JOIN sys.columns c ON c.default_object_id = dc.object_id
INNER JOIN sys.tables t ON t.object_id = c.object_id
INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE s.name = @SchemaName AND t.name = @TableName AND c.name = @ColumnName";

            var existingConstraintName = db.Ado.GetString(
                existingConstraintSql,
                new SugarParameter("@SchemaName", schemaName),
                new SugarParameter("@TableName", pureTableName),
                new SugarParameter("@ColumnName", columnName));

            if (!string.IsNullOrWhiteSpace(existingConstraintName))
            {
                var dropSql = $"ALTER TABLE {QuoteSqlServerTableName(schemaName, pureTableName)} DROP CONSTRAINT {QuoteSqlServerIdentifier(existingConstraintName)}";
                db.Ado.ExecuteCommand(dropSql);
            }

            var constraintName = BuildSqlServerDefaultConstraintName(pureTableName, columnName);
            var addSql = $"ALTER TABLE {QuoteSqlServerTableName(schemaName, pureTableName)} ADD CONSTRAINT {QuoteSqlServerIdentifier(constraintName)} DEFAULT {defaultValue} FOR {QuoteSqlServerIdentifier(columnName)}";
            db.Ado.ExecuteCommand(addSql);

            return new { success = true };
        }, databaseName, DatabaseToolAccess.Schema);
    }

    public object AddTableRemark(
        string tableName,
        string description,
        string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.AddTableRemark(tableName, description), databaseName);

    public object IsAnyTableRemark(string tableName,
        string? databaseName = null)
        => QueryExists(db => db.DbMaintenance.IsAnyTableRemark(tableName), databaseName);

    public object DeleteTableRemark(string tableName,
        string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.DeleteTableRemark(tableName), databaseName);

    public object AddColumnRemark(
        string tableName,
        string columnName,
        string description,
        string? databaseName = null)
    {
        return WithClient(db =>
        {
            if (db.CurrentConnectionConfig.DbType != DbType.SqlServer)
            {
                return new { success = db.DbMaintenance.AddColumnRemark(columnName, tableName, description) };
            }

            var existsSql = @"SELECT COUNT(*)
                    FROM sys.extended_properties ep
                    INNER JOIN sys.tables t ON ep.major_id = t.object_id
                    INNER JOIN sys.columns c ON ep.major_id = c.object_id AND ep.minor_id = c.column_id
                    WHERE t.name = @TableName AND c.name = @ColumnName AND ep.name = 'MS_Description'";

            var exists = db.Ado.GetInt(
                existsSql,
                new SugarParameter("@TableName", tableName),
                new SugarParameter("@ColumnName", columnName)) > 0;

            if (exists)
            {
                var dropSql = @"EXEC sys.sp_dropextendedproperty
                        @name=N'MS_Description',
                        @level0type=N'SCHEMA', @level0name=N'dbo',
                        @level1type=N'TABLE',  @level1name=@TableName,
                        @level2type=N'COLUMN', @level2name=@ColumnName";

                db.Ado.ExecuteCommand(
                    dropSql,
                    new SugarParameter("@TableName", tableName),
                    new SugarParameter("@ColumnName", columnName));
            }

            var addSql = @"EXEC sys.sp_addextendedproperty
                    @name=N'MS_Description',
                    @value=@Description,
                    @level0type=N'SCHEMA', @level0name=N'dbo',
                    @level1type=N'TABLE',  @level1name=@TableName,
                    @level2type=N'COLUMN', @level2name=@ColumnName";

            db.Ado.ExecuteCommand(
                addSql,
                new SugarParameter("@TableName", tableName),
                new SugarParameter("@ColumnName", columnName),
                new SugarParameter("@Description", description));

            return new { success = true };
        }, databaseName, DatabaseToolAccess.Schema);
    }

    public object DeleteColumnRemark(
        string tableName,
        string columnName,
        string? databaseName = null)
    {
        return WithClient(db =>
        {
            if (db.CurrentConnectionConfig.DbType != DbType.SqlServer)
            {
                return new { success = db.DbMaintenance.DeleteColumnRemark(columnName, tableName) };
            }

            var dropSql = @"EXEC sys.sp_dropextendedproperty
                    @name=N'MS_Description',
                    @level0type=N'SCHEMA', @level0name=N'dbo',
                    @level1type=N'TABLE',  @level1name=@TableName,
                    @level2type=N'COLUMN', @level2name=@ColumnName";

            db.Ado.ExecuteCommand(
                dropSql,
                new SugarParameter("@TableName", tableName),
                new SugarParameter("@ColumnName", columnName));

            return new { success = true };
        }, databaseName, DatabaseToolAccess.Schema);
    }

    public object GetProcList(string? databaseName = null) => QueryData(db => db.DbMaintenance.GetProcList(), databaseName);

    public object GetFuncList(string? databaseName = null) => QueryData(db => db.DbMaintenance.GetFuncList(), databaseName);

    public object DropView(string viewName,
        string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.DropView(viewName), databaseName);

    public object DropFunc(string functionName,
        string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.DropFunction(functionName), databaseName);

    public object DropProc(string procedureName,
        string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.DropProc(procedureName), databaseName);

    public object GetTriggerNames(string tableName,
        string? databaseName = null)
        => QueryData(db => db.DbMaintenance.GetTriggerNames(tableName), databaseName);

    public object GetTableSchema(string tableName,
        string? databaseName = null,
        bool refresh = false)
    {
        return WithClientContext(context =>
        {
            SqlSafetyGuard.EnsureSafeTableName(tableName);
            var data = _metadataCache.Get(context, tableName, refresh, () =>
            {
                var columns = context.Client.DbMaintenance.GetColumnInfosByTableName(tableName, false);
                var document = new { tableName, columns,
                    primaryKeys = columns.Where(c => c.IsPrimarykey).Select(c => c.DbColumnName).ToArray(),
                    identityColumns = columns.Where(c => c.IsIdentity).Select(c => c.DbColumnName).ToArray(),
                    indexes = context.Client.DbMaintenance.GetIndexList(tableName) };
                // Bound each cached entry as well as the total number of entries.
                var encoded = BoundedQueryExecutor.EncodeResponse(document, Math.Min(1024 * 1024, ExecutionOptions.MaxResponseBytes));
                using var json = JsonDocument.Parse(encoded.Utf8Json);
                return json.RootElement.Clone();
            });
            return new { success = true, data };
        }, databaseName);
    }

    private object QueryData(Func<ISqlSugarClient, object?> query, string? databaseName)
    {
        return WithClient(db => new
        {
            success = true,
            data = query(db)
        }, databaseName);
    }

    private object QueryExists(Func<ISqlSugarClient, bool> query, string? databaseName)
    {
        return WithClient(db => new
        {
            success = true,
            exists = query(db)
        }, databaseName);
    }

    private object ExecuteOperation(Func<ISqlSugarClient, bool> operation, string? databaseName)
    {
        return WithClient(db => new
        {
            success = operation(db)
        }, databaseName, DatabaseToolAccess.Schema);
    }

    private static (string SchemaName, string TableName) ParseSqlServerObjectName(string tableName)
    {
        SqlSafetyGuard.EnsureSafeTableName(tableName);

        var parts = tableName
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return parts.Length switch
        {
            1 => ("dbo", parts[0]),
            2 => (parts[0], parts[1]),
            _ => throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "tableName 仅支持 table 或 schema.table 形式")
        };
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
