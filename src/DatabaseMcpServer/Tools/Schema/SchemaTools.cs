using System.ComponentModel;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Operations;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;

namespace DatabaseMcpServer.Tools.Management;

[ToolGroup("schema", "sql", DatabaseMcpServer.Helpers.DatabaseToolAccess.Read)]
[McpServerToolType]
internal sealed class SchemaTools(SchemaReadOperations reads, SchemaCommandOperations commands)
{
    [ToolPolicy(Capability = DatabaseMcpServer.Helpers.DatabaseCapability.DatabaseListing)]
    [McpServerTool(Title = "Get Database List", ReadOnly = true)]
    [Description("Call DbMaintenance.GetDataBaseList to retrieve every database the instance can see and return it inside the data array.")]
    public string GetDataBaseList([Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(reads.GetDataBaseList(databaseName));

    [McpServerTool(Title = "Check Table Exists", ReadOnly = true)]
    [Description("Check whether tableName exists in the current database and return an exists boolean.")]
    public string IsAnyTable([Description("Table name")] string tableName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(reads.IsAnyTable(tableName, databaseName));

    [McpServerTool(Title = "Check Column Exists", ReadOnly = true)]
    [Description("Check whether columnName exists on tableName and return an exists boolean.")]
    public string IsAnyColumn(
        [Description("Table name")] string tableName,
        [Description("Column name")] string columnName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(reads.IsAnyColumn(tableName, columnName, databaseName));

    [ToolPolicy(Capability = DatabaseMcpServer.Helpers.DatabaseCapability.ConstraintLookup)]
    [McpServerTool(Title = "Check Constraint Exists", ReadOnly = true)]
    [Description("Check whether the specified constraintName exists (unique, foreign key, or check constraint) and return an exists boolean.")]
    public string IsAnyConstraint([Description("Constraint name")] string constraintName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(reads.IsAnyConstraint(constraintName, databaseName));

    [ToolPolicy(RequiresConfirmation = true, Access = DatabaseMcpServer.Helpers.DatabaseToolAccess.Schema)]
    [McpServerTool(Title = "Create Table", Destructive = true)]
    [Description("Create a table from typed project column definitions: name, dataType, length, precision, scale, nullable, primaryKey, identity, comment, defaultExpression.")]
    public string CreateTable(
        [Description("Table name")] string tableName,
        [Description("Column definitions, e.g. [{\"name\":\"id\",\"dataType\":\"int\",\"primaryKey\":true}].")] ColumnDefinition[] columns,
        [Description("Whether to create primary key constraints for columns marked primaryKey")] bool isCreatePrimaryKey = true,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(commands.CreateTable(tableName, columns, isCreatePrimaryKey, databaseName));

    [ToolPolicy(RequiresConfirmation = true, Access = DatabaseMcpServer.Helpers.DatabaseToolAccess.Schema)]
    [McpServerTool(Title = "Drop Table", Destructive = true)]
    [Description("Immediately drop the specified tableName, removing both structure and data, and return success to indicate completion.")]
    public string DropTable([Description("Table name")] string tableName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(commands.DropTable(tableName, databaseName));

    [ToolPolicy(RequiresConfirmation = true, Access = DatabaseMcpServer.Helpers.DatabaseToolAccess.Schema)]
    [McpServerTool(Title = "Truncate Table", Destructive = true)]
    [Description("Execute TRUNCATE on tableName to delete all rows while preserving the schema; the response includes success.")]
    public string TruncateTable([Description("Table name")] string tableName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(commands.TruncateTable(tableName, databaseName));

    [ToolPolicy(RequiresConfirmation = true, Access = DatabaseMcpServer.Helpers.DatabaseToolAccess.Schema)]
    [McpServerTool(Title = "Backup Table", Destructive = true)]
    [Description("Use the provider's table-copy capability to create newTableName from oldTableName. Not a complete database backup: indexes, constraints, triggers, grants and recovery semantics are provider-dependent.")]
    public string BackupTable(
        [Description("Original table name")] string oldTableName,
        [Description("New table name")] string newTableName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(commands.BackupTable(oldTableName, newTableName, databaseName));

    [ToolPolicy(RequiresConfirmation = true, Access = DatabaseMcpServer.Helpers.DatabaseToolAccess.Schema, Capability = DatabaseMcpServer.Helpers.DatabaseCapability.RenameTable)]
    [McpServerTool(Title = "Rename Table", Destructive = true)]
    [Description("Rename oldTableName to newTableName and return success so callers know the rename succeeded.")]
    public string RenameTable(
        [Description("Original table name")] string oldTableName,
        [Description("New table name")] string newTableName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(commands.RenameTable(oldTableName, newTableName, databaseName));

    [ToolPolicy(RequiresConfirmation = true, Access = DatabaseMcpServer.Helpers.DatabaseToolAccess.Schema)]
    [McpServerTool(Title = "Add Column", Destructive = true)]
    [Description("Add a column using a typed column definition.")]
    public string AddColumn(
        [Description("Table name")] string tableName,
        [Description("Column definition with name, dataType and optional length, precision/scale, nullable, primaryKey, identity, comment or defaultExpression.")] ColumnDefinition column,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(commands.AddColumn(tableName, column, databaseName));

    [ToolPolicy(RequiresConfirmation = true, Access = DatabaseMcpServer.Helpers.DatabaseToolAccess.Schema)]
    [McpServerTool(Title = "Update Column", Destructive = true)]
    [Description("Modify an existing column using a typed column definition.")]
    public string UpdateColumn(
        [Description("Table name")] string tableName,
        [Description("Column definition with name, dataType and optional length, precision/scale, nullable, primaryKey, identity, comment or defaultExpression.")] ColumnDefinition column,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(commands.UpdateColumn(tableName, column, databaseName));

    [ToolPolicy(RequiresConfirmation = true, Access = DatabaseMcpServer.Helpers.DatabaseToolAccess.Schema)]
    [McpServerTool(Title = "Drop Column", Destructive = true)]
    [Description("Drop the specified columnName from tableName and return success.")]
    public string DropColumn(
        [Description("Table name")] string tableName,
        [Description("Column name")] string columnName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(commands.DropColumn(tableName, columnName, databaseName));

    [ToolPolicy(RequiresConfirmation = true, Access = DatabaseMcpServer.Helpers.DatabaseToolAccess.Schema, Capability = DatabaseMcpServer.Helpers.DatabaseCapability.RenameColumn)]
    [McpServerTool(Title = "Rename Column", Destructive = true)]
    [Description("Rename oldColumnName to newColumnName within tableName to adjust field naming.")]
    public string RenameColumn(
        [Description("Table name")] string tableName,
        [Description("Original column name")] string oldColumnName,
        [Description("New column name")] string newColumnName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(commands.RenameColumn(tableName, oldColumnName, newColumnName, databaseName));

    [ToolPolicy(RequiresConfirmation = true, Access = DatabaseMcpServer.Helpers.DatabaseToolAccess.Schema, Capability = DatabaseMcpServer.Helpers.DatabaseCapability.AddPrimaryKey)]
    [McpServerTool(Title = "Add Primary Key", Destructive = true)]
    [Description("Create a primary key constraint on tableName for columnName or composite columnNames.")]
    public string AddPrimaryKey(
        [Description("Table name")] string tableName,
        [Description("Single primary key column name; mutually exclusive with columnNames.")] string? columnName = null,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null,
        [Description("Composite primary key column names (1-32); mutually exclusive with columnName.")] string[]? columnNames = null)
        => McpResultAdapter.Text(commands.AddPrimaryKey(tableName, columnName, databaseName, columnNames));

    [ToolPolicy(RequiresConfirmation = true, Access = DatabaseMcpServer.Helpers.DatabaseToolAccess.Schema, Capability = DatabaseMcpServer.Helpers.DatabaseCapability.DropConstraint)]
    [McpServerTool(Title = "Drop Constraint", Destructive = true)]
    [Description("Drop the specified constraintName from tableName (primary key, unique index, foreign key, etc.) and return success.")]
    public string DropConstraint(
        [Description("Table name")] string tableName,
        [Description("Constraint name")] string constraintName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(commands.DropConstraint(tableName, constraintName, databaseName));

    [ToolPolicy(RequiresConfirmation = true, Access = DatabaseMcpServer.Helpers.DatabaseToolAccess.Schema)]
    [McpServerTool(Title = "Create Index", Destructive = true)]
    [Description("Create an index named indexName on tableName for columnName or composite columnNames; set isUnique to true to build a unique index and return success.")]
    public string CreateIndex(
        [Description("Table name")] string tableName,
        [Description("Index name")] string indexName,
        [Description("Single indexed column name; mutually exclusive with columnNames.")] string? columnName = null,
        [Description("Whether it's a unique index")] bool isUnique = false,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null,
        [Description("Composite indexed column names (1-32); mutually exclusive with columnName.")] string[]? columnNames = null)
        => McpResultAdapter.Text(commands.CreateIndex(tableName, indexName, columnName, isUnique, databaseName, columnNames));

    [ToolPolicy(RequiresConfirmation = true, Access = DatabaseMcpServer.Helpers.DatabaseToolAccess.Schema, Capability = DatabaseMcpServer.Helpers.DatabaseCapability.DropIndex)]
    [McpServerTool(Title = "Drop Index", Destructive = true)]
    [Description("Drop the specified indexName on tableName and return success.")]
    public string DropIndex(
        [Description("Table name")] string tableName,
        [Description("Index name")] string indexName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(commands.DropIndex(tableName, indexName, databaseName));

    [ToolPolicy(RequiresConfirmation = true, Access = DatabaseMcpServer.Helpers.DatabaseToolAccess.Schema, Capability = DatabaseMcpServer.Helpers.DatabaseCapability.DefaultValue)]
    [McpServerTool(Title = "Add Default Value", Destructive = true)]
    [Description("Set defaultValue on tableName.columnName by invoking DbMaintenance.AddDefaultValue.")]
    public string AddDefaultValue(
        [Description("Table name")] string tableName,
        [Description("Column name")] string columnName,
        [Description("Default value")] string defaultValue,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(commands.AddDefaultValue(tableName, columnName, defaultValue, databaseName));

    [ToolPolicy(RequiresConfirmation = true, Access = DatabaseMcpServer.Helpers.DatabaseToolAccess.Schema, Capability = DatabaseMcpServer.Helpers.DatabaseCapability.SchemaComments)]
    [McpServerTool(Title = "Add Table Remark", Destructive = true)]
    [Description("Attach a description to tableName so schema consumers can surface the table’s purpose.")]
    public string AddTableRemark(
        [Description("Table name")] string tableName,
        [Description("Table description")] string description,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(commands.AddTableRemark(tableName, description, databaseName));

    [ToolPolicy(Capability = DatabaseMcpServer.Helpers.DatabaseCapability.TableRemarkLookup)]
    [McpServerTool(Title = "Check Table Remark Exists", ReadOnly = true)]
    [Description("Return exists to show whether tableName already has a stored remark.")]
    public string IsAnyTableRemark([Description("Table name")] string tableName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(reads.IsAnyTableRemark(tableName, databaseName));

    [ToolPolicy(RequiresConfirmation = true, Access = DatabaseMcpServer.Helpers.DatabaseToolAccess.Schema, Capability = DatabaseMcpServer.Helpers.DatabaseCapability.SchemaComments)]
    [McpServerTool(Title = "Delete Table Remark", Destructive = true)]
    [Description("Remove the remark/description associated with tableName and return success.")]
    public string DeleteTableRemark([Description("Table name")] string tableName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(commands.DeleteTableRemark(tableName, databaseName));

    [ToolPolicy(RequiresConfirmation = true, Access = DatabaseMcpServer.Helpers.DatabaseToolAccess.Schema, Capability = DatabaseMcpServer.Helpers.DatabaseCapability.SchemaComments)]
    [McpServerTool(Title = "Add Column Remark", Destructive = true)]
    [Description("Attach a description to tableName.columnName, making the column meaning visible to downstream tools.")]
    public string AddColumnRemark(
        [Description("Table name")] string tableName,
        [Description("Column name")] string columnName,
        [Description("Column description")] string description,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(commands.AddColumnRemark(tableName, columnName, description, databaseName));

    [ToolPolicy(RequiresConfirmation = true, Access = DatabaseMcpServer.Helpers.DatabaseToolAccess.Schema, Capability = DatabaseMcpServer.Helpers.DatabaseCapability.DeleteColumnRemark)]
    [McpServerTool(Title = "Delete Column Remark", Destructive = true)]
    [Description("Delete the stored description for tableName.columnName and return success.")]
    public string DeleteColumnRemark(
        [Description("Table name")] string tableName,
        [Description("Column name")] string columnName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(commands.DeleteColumnRemark(tableName, columnName, databaseName));

    [ToolPolicy(RequiresConfirmation = true, Access = DatabaseMcpServer.Helpers.DatabaseToolAccess.Schema)]
    [McpServerTool(Title = "Drop View", Destructive = true)]
    [Description("Drop the specified viewName definition and return success.")]
    public string DropView([Description("View name")] string viewName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(commands.DropView(viewName, databaseName));

    [ToolPolicy(RequiresConfirmation = true, Access = DatabaseMcpServer.Helpers.DatabaseToolAccess.Schema, Capability = DatabaseMcpServer.Helpers.DatabaseCapability.Routines)]
    [McpServerTool(Title = "Drop Function", Destructive = true)]
    [Description("Drop the database function identified by functionName, typically during cleanup.")]
    public string DropFunc([Description("Function name")] string functionName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(commands.DropFunc(functionName, databaseName));

    [ToolPolicy(RequiresConfirmation = true, Access = DatabaseMcpServer.Helpers.DatabaseToolAccess.Schema, Capability = DatabaseMcpServer.Helpers.DatabaseCapability.Routines)]
    [McpServerTool(Title = "Drop Stored Procedure", Destructive = true)]
    [Description("Drop the stored procedure identified by procedureName and return success.")]
    public string DropProc([Description("Stored procedure name")] string procedureName,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null)
        => McpResultAdapter.Text(commands.DropProc(procedureName, databaseName));

    [ToolPolicy(Analysis = true, Capability = DatabaseMcpServer.Helpers.DatabaseCapability.Sql)]
    [McpServerTool(Title = "Get Table Schema", ReadOnly = true, UseStructuredContent = true, OutputSchemaType = typeof(TableSchemaResult))]
    [Description("Read metadata for one table or 1-50 tables with per-table status and shared output budgets. Native readers support asynchronous cancellation. Other SQL providers use synchronous driver metadata for basic sections. Refresh bypasses the metadata cache.")]
    public Task<CallToolResult> GetTableSchema([Description("Single table name; mutually exclusive with tableNames.")] string? tableName = null,
        [Description("Optional database connection name. Does not change the current database.")] string? databaseName = null,
        [Description("Bypass the metadata cache.")] bool refresh = false,
        [SchemaSectionsDescription] string[]? sections = null,
        [Description("1-50 distinct tables, mutually exclusive with tableName.")] string[]? tableNames = null,
        [Description("Shared metadata entry budget including nested arrays.")] int? maxRows = null,
        [Description("Shared tool JSON UTF-8 byte budget.")] int? maxResponseBytes = null,
        [Description("Total request timeout, including queue time.")] int? commandTimeoutSeconds = null,
        CancellationToken cancellationToken = default)
        => McpResultAdapter.Structured(reads.GetTableSchema(tableName, databaseName, refresh, sections, tableNames, maxRows, maxResponseBytes, commandTimeoutSeconds, cancellationToken));
}
