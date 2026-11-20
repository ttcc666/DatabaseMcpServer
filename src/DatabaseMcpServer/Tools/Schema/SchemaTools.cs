using System.ComponentModel;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Operations;
using ModelContextProtocol.Server;

namespace DatabaseMcpServer.Tools.Management;

[McpServerToolType]
internal sealed class SchemaTools(SchemaOperations operations)
{
    [McpServerTool(ReadOnly = true)]
    [Description("Call DbMaintenance.GetDataBaseList to retrieve every database the instance can see and return it inside the data array.")]
    public string GetDataBaseList([Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.GetDataBaseList(databaseName));

    [McpServerTool(ReadOnly = true)]
    [Description("Return fresh basic view metadata (Name, Description, DbObjectType). View definitions and schemas are not included.")]
    public string GetViewInfoList([Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.GetViewInfoList(databaseName));

    [McpServerTool(ReadOnly = true)]
    [Description("Return fresh basic table information (Name, Description, DbObjectType) inside data.")]
    public string GetTableInfoList([Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.GetTableInfoList(databaseName));

    [McpServerTool(ReadOnly = true)]
    [Description("Accept a tableName and return every column with data type, length, nullable flag, and other metadata via DbMaintenance.GetColumnInfosByTableName.")]
    public string GetColumnInfosByTableName([Description("Table name")] string tableName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.GetColumnInfosByTableName(tableName, databaseName));

    [McpServerTool(ReadOnly = true)]
    [Description("Return all identity (auto-increment) columns for the provided tableName so callers know whether an identity key exists.")]
    public string GetIsIdentities([Description("Table name")] string tableName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.GetIsIdentities(tableName, databaseName));

    [McpServerTool(ReadOnly = true)]
    [Description("Return primary key column names for tableName as a string array. Constraint names and composite key ordinal order are not provided.")]
    public string GetPrimaries([Description("Table name")] string tableName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.GetPrimaries(tableName, databaseName));

    [McpServerTool(ReadOnly = true)]
    [Description("Check whether tableName exists in the current database and return an exists boolean.")]
    public string IsAnyTable([Description("Table name")] string tableName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.IsAnyTable(tableName, databaseName));

    [McpServerTool(ReadOnly = true)]
    [Description("Check whether columnName exists on tableName and return an exists boolean.")]
    public string IsAnyColumn(
        [Description("Table name")] string tableName,
        [Description("Column name")] string columnName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.IsAnyColumn(tableName, columnName, databaseName));

    [McpServerTool(ReadOnly = true)]
    [Description("Check whether the specified constraintName exists (unique, foreign key, or check constraint) and return an exists boolean.")]
    public string IsAnyConstraint([Description("Constraint name")] string constraintName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.IsAnyConstraint(constraintName, databaseName));

    [ToolPolicy(RequiresConfirmation = true)]
    [McpServerTool(Destructive = true)]
    [Description("Create tableName using columnsInfo JSON array of DbColumnInfo-compatible definitions (DbColumnName, DataType, Length, IsNullable, IsPrimarykey, IsIdentity, etc.).")]
    public string CreateTable(
        [Description("Table name")] string tableName,
        [Description("Columns info JSON array. Example: [{\"DbColumnName\":\"id\",\"DataType\":\"int\",\"IsPrimarykey\":true},{\"DbColumnName\":\"name\",\"DataType\":\"nvarchar\",\"Length\":100}]")] string columnsInfo,
        [Description("Whether to create primary key constraints for columns marked IsPrimarykey")] bool isCreatePrimaryKey = true,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.CreateTable(tableName, columnsInfo, isCreatePrimaryKey, databaseName));

    [ToolPolicy(RequiresConfirmation = true)]
    [McpServerTool(Destructive = true)]
    [Description("Immediately drop the specified tableName, removing both structure and data, and return success to indicate completion.")]
    public string DropTable([Description("Table name")] string tableName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.DropTable(tableName, databaseName));

    [ToolPolicy(RequiresConfirmation = true)]
    [McpServerTool(Destructive = true)]
    [Description("Execute TRUNCATE on tableName to delete all rows while preserving the schema; the response includes success.")]
    public string TruncateTable([Description("Table name")] string tableName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.TruncateTable(tableName, databaseName));

    [ToolPolicy(RequiresConfirmation = true)]
    [McpServerTool(Destructive = true)]
    [Description("Use DbMaintenance.BackupTable to copy oldTableName to newTableName, duplicating both schema and current data.")]
    public string BackupTable(
        [Description("Original table name")] string oldTableName,
        [Description("New table name")] string newTableName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.BackupTable(oldTableName, newTableName, databaseName));

    [ToolPolicy(RequiresConfirmation = true)]
    [McpServerTool(Destructive = true)]
    [Description("Rename oldTableName to newTableName and return success so callers know the rename succeeded.")]
    public string RenameTable(
        [Description("Original table name")] string oldTableName,
        [Description("New table name")] string newTableName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.RenameTable(oldTableName, newTableName, databaseName));

    [ToolPolicy(RequiresConfirmation = true)]
    [McpServerTool(Destructive = true)]
    [Description("Add a column on tableName using columnInfo JSON (DbColumnName, DataType, Length, IsNullable, DecimalDigits, etc.) to describe the new definition.")]
    public string AddColumn(
        [Description("Table name")] string tableName,
        [Description("Column info JSON with properties like DbColumnName, DataType, Length, IsNullable, DecimalDigits")] string columnInfo,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.AddColumn(tableName, columnInfo, databaseName));

    [ToolPolicy(RequiresConfirmation = true)]
    [McpServerTool(Destructive = true)]
    [Description("Modify an existing column on tableName using the supplied columnInfo JSON to specify DbColumnName, DataType, Length, IsNullable, DecimalDigits, and other properties.")]
    public string UpdateColumn(
        [Description("Table name")] string tableName,
        [Description("Column info JSON with properties like DbColumnName, DataType, Length, IsNullable, DecimalDigits")] string columnInfo,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.UpdateColumn(tableName, columnInfo, databaseName));

    [ToolPolicy(RequiresConfirmation = true)]
    [McpServerTool(Destructive = true)]
    [Description("Drop the specified columnName from tableName and return success.")]
    public string DropColumn(
        [Description("Table name")] string tableName,
        [Description("Column name")] string columnName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.DropColumn(tableName, columnName, databaseName));

    [ToolPolicy(RequiresConfirmation = true)]
    [McpServerTool(Destructive = true)]
    [Description("Rename oldColumnName to newColumnName within tableName to adjust field naming.")]
    public string RenameColumn(
        [Description("Table name")] string tableName,
        [Description("Original column name")] string oldColumnName,
        [Description("New column name")] string newColumnName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.RenameColumn(tableName, oldColumnName, newColumnName, databaseName));

    [ToolPolicy(RequiresConfirmation = true)]
    [McpServerTool(Destructive = true)]
    [Description("Create a primary key constraint on tableName for columnName, useful when introducing a single-column key.")]
    public string AddPrimaryKey(
        [Description("Table name")] string tableName,
        [Description("Column name")] string columnName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.AddPrimaryKey(tableName, columnName, databaseName));

    [ToolPolicy(RequiresConfirmation = true)]
    [McpServerTool(Destructive = true)]
    [Description("Drop the specified constraintName from tableName (primary key, unique index, foreign key, etc.) and return success.")]
    public string DropConstraint(
        [Description("Table name")] string tableName,
        [Description("Constraint name")] string constraintName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.DropConstraint(tableName, constraintName, databaseName));

    [ToolPolicy(RequiresConfirmation = true)]
    [McpServerTool(Destructive = true)]
    [Description("Create an index named indexName for tableName.columnName; set isUnique to true to build a unique index and return success.")]
    public string CreateIndex(
        [Description("Table name")] string tableName,
        [Description("Index name")] string indexName,
        [Description("Column name")] string columnName,
        [Description("Whether it's a unique index")] bool isUnique = false,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.CreateIndex(tableName, indexName, columnName, isUnique, databaseName));

    [McpServerTool(ReadOnly = true)]
    [Description("List every index defined on tableName (name and attributes) and return it in the data array.")]
    public string GetIndexList([Description("Table name")] string tableName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.GetIndexList(tableName, databaseName));

    [ToolPolicy(RequiresConfirmation = true)]
    [McpServerTool(Destructive = true)]
    [Description("Set defaultValue on tableName.columnName by invoking DbMaintenance.AddDefaultValue.")]
    public string AddDefaultValue(
        [Description("Table name")] string tableName,
        [Description("Column name")] string columnName,
        [Description("Default value")] string defaultValue,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.AddDefaultValue(tableName, columnName, defaultValue, databaseName));

    [ToolPolicy(RequiresConfirmation = true)]
    [McpServerTool(Destructive = true)]
    [Description("Attach a description to tableName so schema consumers can surface the table’s purpose.")]
    public string AddTableRemark(
        [Description("Table name")] string tableName,
        [Description("Table description")] string description,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.AddTableRemark(tableName, description, databaseName));

    [McpServerTool(ReadOnly = true)]
    [Description("Return exists to show whether tableName already has a stored remark.")]
    public string IsAnyTableRemark([Description("Table name")] string tableName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.IsAnyTableRemark(tableName, databaseName));

    [ToolPolicy(RequiresConfirmation = true)]
    [McpServerTool(Destructive = true)]
    [Description("Remove the remark/description associated with tableName and return success.")]
    public string DeleteTableRemark([Description("Table name")] string tableName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.DeleteTableRemark(tableName, databaseName));

    [ToolPolicy(RequiresConfirmation = true)]
    [McpServerTool(Destructive = true)]
    [Description("Attach a description to tableName.columnName, making the column meaning visible to downstream tools.")]
    public string AddColumnRemark(
        [Description("Table name")] string tableName,
        [Description("Column name")] string columnName,
        [Description("Column description")] string description,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.AddColumnRemark(tableName, columnName, description, databaseName));

    [ToolPolicy(RequiresConfirmation = true)]
    [McpServerTool(Destructive = true)]
    [Description("Delete the stored description for tableName.columnName and return success.")]
    public string DeleteColumnRemark(
        [Description("Table name")] string tableName,
        [Description("Column name")] string columnName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.DeleteColumnRemark(tableName, columnName, databaseName));

    [McpServerTool(ReadOnly = true)]
    [Description("List every stored procedure name in the current database and place the collection in data.")]
    public string GetProcList([Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.GetProcList(databaseName));

    [McpServerTool(ReadOnly = true)]
    [Description("List every database function name so callers can audit reusable logic.")]
    public string GetFuncList([Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.GetFuncList(databaseName));

    [ToolPolicy(RequiresConfirmation = true)]
    [McpServerTool(Destructive = true)]
    [Description("Drop the specified viewName definition and return success.")]
    public string DropView([Description("View name")] string viewName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.DropView(viewName, databaseName));

    [ToolPolicy(RequiresConfirmation = true)]
    [McpServerTool(Destructive = true)]
    [Description("Drop the database function identified by functionName, typically during cleanup.")]
    public string DropFunc([Description("Function name")] string functionName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.DropFunc(functionName, databaseName));

    [ToolPolicy(RequiresConfirmation = true)]
    [McpServerTool(Destructive = true)]
    [Description("Drop the stored procedure identified by procedureName and return success.")]
    public string DropProc([Description("Stored procedure name")] string procedureName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.DropProc(procedureName, databaseName));

    [McpServerTool(ReadOnly = true)]
    [Description("List every trigger defined on tableName to expose side effects that may fire on DML.")]
    public string GetTriggerNames([Description("Table name")] string tableName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(operations.GetTriggerNames(tableName, databaseName));

    [McpServerTool(ReadOnly = true)]
    [Description("Return columns, primary keys, identity columns and indexes for one table. Cached up to 30 seconds by default; use refresh after external DDL. Server DDL invalidates the target cache.")]
    public string GetTableSchema([Description("Table name")] string tableName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null,
        [Description("Bypass the metadata cache.")] bool refresh = false)
        => McpResultAdapter.Text(operations.GetTableSchema(tableName, databaseName, refresh));
}
