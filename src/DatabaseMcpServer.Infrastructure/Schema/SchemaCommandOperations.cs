using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Schema;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Operations;

internal sealed class SchemaCommandOperations : SqlOperationExecutor
{
    public SchemaCommandOperations(
        IDatabaseConfigService databaseConfig,
        IDatabaseHelperService databaseHelper,
        ILogger<SchemaCommandOperations> logger, DatabaseMetadataCache metadataCache, DatabaseExecutionOptions executionOptions, SqlDatabaseSessions sessions)
        : base(databaseConfig, databaseHelper, logger, executionOptions, metadataCache, sessions)
    {
    }

    public object CreateTable(
        string tableName,
        ColumnDefinition[] columns,
        bool isCreatePrimaryKey = true,
        string? databaseName = null)
    {
        return WithClient(db =>
        {
            SqlSafetyGuard.EnsureSafeTableName(tableName);
            return new { success = db.DbMaintenance.CreateTable(tableName, SchemaColumnDefinitionParser.ParseMany(columns), isCreatePrimaryKey) };
        }, databaseName, DatabaseToolAccess.Schema);
    }

    public object DropTable(string tableName,
        string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.DropTable(tableName), databaseName);

    public object TruncateTable(string tableName,
        string? databaseName = null)
        => ExecuteOperation(db => db.CurrentConnectionConfig.DbType == DbType.Sqlite
            ? TruncateSqliteTable(db, tableName)
            : db.DbMaintenance.TruncateTable(tableName), databaseName);

    public object BackupTable(
        string oldTableName,
        string newTableName,
        string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.BackupTable(oldTableName, newTableName), databaseName);

    public object RenameTable(
        string oldTableName,
        string newTableName,
        string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.RenameTable(oldTableName, newTableName), databaseName, DatabaseCapability.RenameTable);

    public object AddColumn(
        string tableName,
        ColumnDefinition column,
        string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.AddColumn(tableName, SchemaColumnDefinitionParser.Parse(column)), databaseName);

    public object UpdateColumn(
        string tableName,
        ColumnDefinition column,
        string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.UpdateColumn(tableName, SchemaColumnDefinitionParser.Parse(column)), databaseName);

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
        => ExecuteOperation(db => db.DbMaintenance.RenameColumn(tableName, oldColumnName, newColumnName), databaseName, DatabaseCapability.RenameColumn);

    public object AddPrimaryKey(
        string tableName,
        string? columnName = null,
        string? databaseName = null,
        string[]? columnNames = null)
        => ExecuteOperation(db =>
        {
            var cols = ResolveIndexColumns(columnName, columnNames);
            return cols.Length == 1
                ? db.DbMaintenance.AddPrimaryKey(tableName, cols[0])
                : db.DbMaintenance.AddPrimaryKeys(tableName, cols);
        }, databaseName, DatabaseCapability.AddPrimaryKey);

    public object DropConstraint(
        string tableName,
        string constraintName,
        string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.DropConstraint(tableName, constraintName), databaseName, DatabaseCapability.DropConstraint);

    public object CreateIndex(
        string tableName,
        string indexName,
        string? columnName = null,
        bool isUnique = false,
        string? databaseName = null,
        string[]? columnNames = null)
        => ExecuteOperation(db => db.DbMaintenance.CreateIndex(tableName, ResolveIndexColumns(columnName, columnNames), indexName, isUnique), databaseName);

    public object DropIndex(
        string tableName,
        string indexName,
        string? databaseName = null)
        => ExecuteOperation(db =>
        {
            SqlSafetyGuard.EnsureSafeTableName(tableName);
            SqlSafetyGuard.EnsureSafeTableName(indexName);
            return db.DbMaintenance.DropIndex(indexName, tableName);
        }, databaseName, DatabaseCapability.DropIndex);

    public object AddDefaultValue(
        string tableName,
        string columnName,
        string defaultValue,
        string? databaseName = null)
        => ExecuteOperation(db =>
        {
            if (string.IsNullOrWhiteSpace(defaultValue))
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "defaultValue 不能为空");
            return db.CurrentConnectionConfig.DbType == DbType.SqlServer
                ? SqlServerSchemaDialect.SetDefaultValue(db, tableName, columnName, defaultValue)
                : db.DbMaintenance.AddDefaultValue(tableName, columnName, defaultValue);
        }, databaseName, DatabaseCapability.DefaultValue);

    public object AddTableRemark(
        string tableName,
        string description,
        string? databaseName = null)
        => ExecuteOperation(db => db.CurrentConnectionConfig.DbType == DbType.SqlServer
            ? SqlServerSchemaDialect.SetTableRemark(db, tableName, description)
            : db.DbMaintenance.AddTableRemark(tableName, description), databaseName, DatabaseCapability.SchemaComments);

    public object DeleteTableRemark(string tableName,
        string? databaseName = null)
        => ExecuteOperation(db => db.CurrentConnectionConfig.DbType == DbType.SqlServer
            ? SqlServerSchemaDialect.DeleteTableRemark(db, tableName)
            : db.DbMaintenance.DeleteTableRemark(tableName), databaseName, DatabaseCapability.SchemaComments);

    public object AddColumnRemark(
        string tableName,
        string columnName,
        string description,
        string? databaseName = null)
        => ExecuteOperation(db => db.CurrentConnectionConfig.DbType != DbType.SqlServer
            ? db.DbMaintenance.AddColumnRemark(columnName, tableName, description)
            : SqlServerSchemaDialect.SetColumnRemark(db, tableName, columnName, description),
            databaseName, DatabaseCapability.SchemaComments);

    public object DeleteColumnRemark(
        string tableName,
        string columnName,
        string? databaseName = null)
        => ExecuteOperation(db => db.CurrentConnectionConfig.DbType != DbType.SqlServer
            ? db.DbMaintenance.DeleteColumnRemark(columnName, tableName)
            : SqlServerSchemaDialect.DeleteColumnRemark(db, tableName, columnName),
            databaseName, DatabaseCapability.DeleteColumnRemark);

    public object DropView(string viewName,
        string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.DropView(viewName), databaseName);

    public object DropFunc(string functionName,
        string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.DropFunction(functionName), databaseName, DatabaseCapability.Routines);

    public object DropProc(string procedureName,
        string? databaseName = null)
        => ExecuteOperation(db => db.DbMaintenance.DropProc(procedureName), databaseName, DatabaseCapability.Routines);

    private object ExecuteOperation(Func<ISqlSugarClient, bool> operation, string? databaseName, DatabaseCapability capability = DatabaseCapability.Sql)
    {
        return WithClientContext(context =>
        {
            ProviderCapabilities.Require(DatabaseProviderCatalog.Parse(context.DatabaseType), capability);
            return new { success = operation(context.Client) };
        }, databaseName, DatabaseToolAccess.Schema);
    }

    private static string[] ResolveIndexColumns(string? columnName, string[]? columnNames)
    {
        if ((columnName == null) == (columnNames == null))
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "请指定 columnName 或 columnNames（不可同时指定或同时省略）。");
        var cols = columnNames ?? [columnName!];
        if (cols.Length == 0 || cols.Length > 32)
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "列数组必须包含 1-32 个字段。");
        foreach (var col in cols)
        {
            SqlSafetyGuard.EnsureSafeTableName(col);
            if (col.Contains('.'))
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "列名不能包含点号。");
        }
        return cols;
    }

    private static bool TruncateSqliteTable(ISqlSugarClient db, string tableName)
    {
        SqlSafetyGuard.EnsureSafeTableName(tableName);
        var quoted = SqlSafetyGuard.QuoteTableName(tableName, DbType.Sqlite);
        var parts = tableName.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var schema = SqlSafetyGuard.QuoteTableName(parts.Length == 1 ? "main" : parts[0], DbType.Sqlite);
        var ownsTransaction = db.Ado.Transaction == null;
        if (ownsTransaction) db.Ado.BeginTran();
        try
        {
            db.Ado.ExecuteCommand($"DELETE FROM {quoted}");
            if (db.Ado.GetInt($"SELECT COUNT(*) FROM {schema}.sqlite_schema WHERE type='table' AND name='sqlite_sequence'") > 0)
                db.Ado.ExecuteCommand($"DELETE FROM {schema}.sqlite_sequence WHERE name=@name COLLATE NOCASE", new SugarParameter("@name", parts[^1]));
            if (ownsTransaction) db.Ado.CommitTran();
            return true;
        }
        catch
        {
            if (ownsTransaction) db.Ado.RollbackTran();
            throw;
        }
    }

}
