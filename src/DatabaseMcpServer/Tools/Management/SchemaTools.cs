using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Tools;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using SqlSugar;
using System.ComponentModel;
using System.Text.Json;
using DatabaseMcpServer.Services;

namespace DatabaseMcpServer.Tools.Management;

/// <summary>
/// 数据库架构管理工具类，用于执行数据库架构相关操作。
/// </summary>
[McpServerToolType]
internal class SchemaTools : McpToolBase
{
    private readonly DatabaseMetadataCache _metadataCache;
    public SchemaTools(
        IDatabaseConfigService databaseConfig,
        IDatabaseHelperService databaseHelper,
        IJsonResultSerializer resultSerializer,
        ILogger<SchemaTools> logger, DatabaseMetadataCache metadataCache, DatabaseExecutionOptions executionOptions)
        : base(databaseConfig, databaseHelper, resultSerializer, logger, executionOptions, metadataCache)
    {
        _metadataCache = metadataCache;
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Call DbMaintenance.GetDataBaseList to retrieve every database the instance can see and return it inside the data array.")]
    public string GetDataBaseList([Description("Optional database connection name. Does not change the current database.")] string? databaseName = null) => QueryData(db => db.DbMaintenance.GetDataBaseList(), databaseName);

    [McpServerTool(ReadOnly = true)]
    [Description("Return fresh basic view metadata (Name, Description, DbObjectType). View definitions and schemas are not included.")]
    public string GetViewInfoList([Description("Optional database connection name. Does not change the current database.")] string? databaseName = null) => QueryData(db => db.DbMaintenance.GetViewInfoList(false), databaseName);

    [McpServerTool(ReadOnly = true)]
    [Description("Return fresh basic table information (Name, Description, DbObjectType) inside data.")]
    public string GetTableInfoList([Description("Optional database connection name. Does not change the current database.")] string? databaseName = null) => QueryData(db => db.DbMaintenance.GetTableInfoList(false), databaseName);

    [McpServerTool(ReadOnly = true)]
    [Description("Accept a tableName and return every column with data type, length, nullable flag, and other metadata via DbMaintenance.GetColumnInfosByTableName.")]
    public string GetColumnInfosByTableName([Description("Table name")] string tableName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => QueryData(db => db.DbMaintenance.GetColumnInfosByTableName(tableName, false), databaseName);

    [McpServerTool(ReadOnly = true)]
    [Description("Return all identity (auto-increment) columns for the provided tableName so callers know whether an identity key exists.")]
    public string GetIsIdentities([Description("Table name")] string tableName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => QueryData(db => db.DbMaintenance.GetColumnInfosByTableName(tableName, false)
            .Where(column => column.IsIdentity).Select(column => column.DbColumnName).ToList(), databaseName);

    [McpServerTool(ReadOnly = true)]
    [Description("Return primary key column names for tableName as a string array. Constraint names and composite key ordinal order are not provided.")]
    public string GetPrimaries([Description("Table name")] string tableName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => QueryData(db => db.DbMaintenance.GetColumnInfosByTableName(tableName, false)
            .Where(column => column.IsPrimarykey).Select(column => column.DbColumnName).ToList(), databaseName);

    [McpServerTool(ReadOnly = true)]
    [Description("Check whether tableName exists in the current database and return an exists boolean.")]
    public string IsAnyTable([Description("Table name")] string tableName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => QueryExists(db => db.DbMaintenance.IsAnyTable(tableName, false), databaseName);

    [McpServerTool(ReadOnly = true)]
    [Description("Check whether columnName exists on tableName and return an exists boolean.")]
    public string IsAnyColumn(
        [Description("Table name")] string tableName,
        [Description("Column name")] string columnName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => QueryExists(db => db.DbMaintenance.IsAnyColumn(tableName, columnName, false), databaseName);

    [McpServerTool(ReadOnly = true)]
    [Description("Check whether the specified constraintName exists (unique, foreign key, or check constraint) and return an exists boolean.")]
    public string IsAnyConstraint([Description("Constraint name")] string constraintName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => QueryExists(db => db.DbMaintenance.IsAnyConstraint(constraintName), databaseName);

    [McpServerTool(Destructive = true)]
    [Description("Create tableName using columnsInfo JSON array of DbColumnInfo-compatible definitions (DbColumnName, DataType, Length, IsNullable, IsPrimarykey, IsIdentity, etc.).")]
    public string CreateTable(
        [Description("Table name")] string tableName,
        [Description("Columns info JSON array. Example: [{\"DbColumnName\":\"id\",\"DataType\":\"int\",\"IsPrimarykey\":true},{\"DbColumnName\":\"name\",\"DataType\":\"nvarchar\",\"Length\":100}]")] string columnsInfo,
        [Description("Whether to create primary key constraints for columns marked IsPrimarykey")] bool isCreatePrimaryKey = true,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
    {
        return WithClient(db =>
        {
            SqlSafetyGuard.EnsureSafeTableName(tableName);
            var columns = SchemaColumnDefinitionParser.ParseMany(columnsInfo);
            return new { success = db.DbMaintenance.CreateTable(tableName, columns, isCreatePrimaryKey) };
        }, databaseName, DatabaseToolAccess.Schema);
    }

    [McpServerTool(Destructive = true)]
    [Description("Immediately drop the specified tableName, removing both structure and data, and return success to indicate completion.")]
    public string DropTable([Description("Table name")] string tableName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.DropTable(tableName), databaseName);

    [McpServerTool(Destructive = true)]
    [Description("Execute TRUNCATE on tableName to delete all rows while preserving the schema; the response includes success.")]
    public string TruncateTable([Description("Table name")] string tableName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.TruncateTable(tableName), databaseName);

    [McpServerTool(Destructive = true)]
    [Description("Use DbMaintenance.BackupTable to copy oldTableName to newTableName, duplicating both schema and current data.")]
    public string BackupTable(
        [Description("Original table name")] string oldTableName,
        [Description("New table name")] string newTableName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.BackupTable(oldTableName, newTableName), databaseName);

    [McpServerTool(Destructive = true)]
    [Description("Rename oldTableName to newTableName and return success so callers know the rename succeeded.")]
    public string RenameTable(
        [Description("Original table name")] string oldTableName,
        [Description("New table name")] string newTableName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.RenameTable(oldTableName, newTableName), databaseName);

    [McpServerTool(Destructive = true)]
    [Description("Add a column on tableName using columnInfo JSON (DbColumnName, DataType, Length, IsNullable, DecimalDigits, etc.) to describe the new definition.")]
    public string AddColumn(
        [Description("Table name")] string tableName,
        [Description("Column info JSON with properties like DbColumnName, DataType, Length, IsNullable, DecimalDigits")] string columnInfo,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.AddColumn(tableName, SchemaColumnDefinitionParser.Parse(columnInfo)), databaseName);

    [McpServerTool(Destructive = true)]
    [Description("Modify an existing column on tableName using the supplied columnInfo JSON to specify DbColumnName, DataType, Length, IsNullable, DecimalDigits, and other properties.")]
    public string UpdateColumn(
        [Description("Table name")] string tableName,
        [Description("Column info JSON with properties like DbColumnName, DataType, Length, IsNullable, DecimalDigits")] string columnInfo,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.UpdateColumn(tableName, SchemaColumnDefinitionParser.Parse(columnInfo)), databaseName);

    [McpServerTool(Destructive = true)]
    [Description("Drop the specified columnName from tableName and return success.")]
    public string DropColumn(
        [Description("Table name")] string tableName,
        [Description("Column name")] string columnName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.DropColumn(tableName, columnName), databaseName);

    [McpServerTool(Destructive = true)]
    [Description("Rename oldColumnName to newColumnName within tableName to adjust field naming.")]
    public string RenameColumn(
        [Description("Table name")] string tableName,
        [Description("Original column name")] string oldColumnName,
        [Description("New column name")] string newColumnName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.RenameColumn(tableName, oldColumnName, newColumnName), databaseName);

    [McpServerTool(Destructive = true)]
    [Description("Create a primary key constraint on tableName for columnName, useful when introducing a single-column key.")]
    public string AddPrimaryKey(
        [Description("Table name")] string tableName,
        [Description("Column name")] string columnName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.AddPrimaryKey(tableName, columnName), databaseName);

    [McpServerTool(Destructive = true)]
    [Description("Drop the specified constraintName from tableName (primary key, unique index, foreign key, etc.) and return success.")]
    public string DropConstraint(
        [Description("Table name")] string tableName,
        [Description("Constraint name")] string constraintName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.DropConstraint(tableName, constraintName), databaseName);

    [McpServerTool(Destructive = true)]
    [Description("Create an index named indexName for tableName.columnName; set isUnique to true to build a unique index and return success.")]
    public string CreateIndex(
        [Description("Table name")] string tableName,
        [Description("Index name")] string indexName,
        [Description("Column name")] string columnName,
        [Description("Whether it's a unique index")] bool isUnique = false,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.CreateIndex(tableName, [columnName], indexName, isUnique), databaseName);

    [McpServerTool(ReadOnly = true)]
    [Description("List every index defined on tableName (name and attributes) and return it in the data array.")]
    public string GetIndexList([Description("Table name")] string tableName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => QueryData(db => db.DbMaintenance.GetIndexList(tableName), databaseName);

    [McpServerTool(Destructive = true)]
    [Description("Set defaultValue on tableName.columnName by invoking DbMaintenance.AddDefaultValue.")]
    public string AddDefaultValue(
        [Description("Table name")] string tableName,
        [Description("Column name")] string columnName,
        [Description("Default value")] string defaultValue,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
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

    [McpServerTool(Destructive = true)]
    [Description("Attach a description to tableName so schema consumers can surface the table’s purpose.")]
    public string AddTableRemark(
        [Description("Table name")] string tableName,
        [Description("Table description")] string description,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.AddTableRemark(tableName, description), databaseName);

    [McpServerTool(ReadOnly = true)]
    [Description("Return exists to show whether tableName already has a stored remark.")]
    public string IsAnyTableRemark([Description("Table name")] string tableName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => QueryExists(db => db.DbMaintenance.IsAnyTableRemark(tableName), databaseName);

    [McpServerTool(Destructive = true)]
    [Description("Remove the remark/description associated with tableName and return success.")]
    public string DeleteTableRemark([Description("Table name")] string tableName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.DeleteTableRemark(tableName), databaseName);

    [McpServerTool(Destructive = true)]
    [Description("Attach a description to tableName.columnName, making the column meaning visible to downstream tools.")]
    public string AddColumnRemark(
        [Description("Table name")] string tableName,
        [Description("Column name")] string columnName,
        [Description("Column description")] string description,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
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

    [McpServerTool(Destructive = true)]
    [Description("Delete the stored description for tableName.columnName and return success.")]
    public string DeleteColumnRemark(
        [Description("Table name")] string tableName,
        [Description("Column name")] string columnName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
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

    [McpServerTool(ReadOnly = true)]
    [Description("List every stored procedure name in the current database and place the collection in data.")]
    public string GetProcList([Description("Optional database connection name. Does not change the current database.")] string? databaseName = null) => QueryData(db => db.DbMaintenance.GetProcList(), databaseName);

    [McpServerTool(ReadOnly = true)]
    [Description("List every database function name so callers can audit reusable logic.")]
    public string GetFuncList([Description("Optional database connection name. Does not change the current database.")] string? databaseName = null) => QueryData(db => db.DbMaintenance.GetFuncList(), databaseName);

    [McpServerTool(Destructive = true)]
    [Description("Drop the specified viewName definition and return success.")]
    public string DropView([Description("View name")] string viewName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.DropView(viewName), databaseName);

    [McpServerTool(Destructive = true)]
    [Description("Drop the database function identified by functionName, typically during cleanup.")]
    public string DropFunc([Description("Function name")] string functionName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.DropFunction(functionName), databaseName);

    [McpServerTool(Destructive = true)]
    [Description("Drop the stored procedure identified by procedureName and return success.")]
    public string DropProc([Description("Stored procedure name")] string procedureName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.DropProc(procedureName), databaseName);

    [McpServerTool(ReadOnly = true)]
    [Description("List every trigger defined on tableName to expose side effects that may fire on DML.")]
    public string GetTriggerNames([Description("Table name")] string tableName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => QueryData(db => db.DbMaintenance.GetTriggerNames(tableName), databaseName);

    [McpServerTool(ReadOnly = true)]
    [Description("Return columns, primary keys, identity columns and indexes for one table. Cached up to 30 seconds by default; use refresh after external DDL. Server DDL invalidates the target cache.")]
    public string GetTableSchema([Description("Table name")] string tableName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null,
        [Description("Bypass the metadata cache.")] bool refresh = false)
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

    private string QueryData(Func<ISqlSugarClient, object?> query, string? databaseName)
    {
        return WithClient(db => new
        {
            success = true,
            data = query(db)
        }, databaseName);
    }

    private string QueryExists(Func<ISqlSugarClient, bool> query, string? databaseName)
    {
        return WithClient(db => new
        {
            success = true,
            exists = query(db)
        }, databaseName);
    }

    private string ExecuteOperation(Func<ISqlSugarClient, bool> operation, string? databaseName)
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
