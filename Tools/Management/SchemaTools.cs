using DatabaseMcpServer.Filters;
using DatabaseMcpServer.Interfaces;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using SqlSugar;
using System.ComponentModel;
using System.Text.Json;

namespace DatabaseMcpServer.Tools.Management;

/// <summary>
/// 数据库架构管理工具类，用于执行数据库架构相关操作。
/// </summary>
internal class SchemaTools
{
    private readonly IDatabaseConfigService _databaseConfig;
    private readonly IDatabaseHelperService _databaseHelper;
    private readonly ILogger<SchemaTools> _logger;

    public SchemaTools(IDatabaseConfigService databaseConfig, IDatabaseHelperService databaseHelper, ILogger<SchemaTools> logger)
    {
        _databaseConfig = databaseConfig;
        _databaseHelper = databaseHelper;
        _logger = logger;
    }

    #region 数据库信息查询

    [McpServerTool]
    [Description("Call DbMaintenance.GetDataBaseList to retrieve every database the instance can see and return it inside the data array.")]
    public string GetDataBaseList()
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var databases = db.DbMaintenance.GetDataBaseList();
            return _databaseHelper.SerializeResult(new { success = true, data = databases });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Use DbMaintenance.GetViewInfoList to pull metadata for every view (name, definition, schema) so clients can inventory logical objects.")]
    public string GetViewInfoList()
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var views = db.DbMaintenance.GetViewInfoList();
            return _databaseHelper.SerializeResult(new { success = true, data = views });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Use DbMaintenance.GetTableInfoList(false) to return basic information for each table (name, description, creation time) inside data.")]
    public string GetTableInfoList()
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var tables = db.DbMaintenance.GetTableInfoList(false);
            return _databaseHelper.SerializeResult(new { success = true, data = tables });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Accept a tableName and return every column with data type, length, nullable flag, and other metadata via DbMaintenance.GetColumnInfosByTableName.")]
    public string GetColumnInfosByTableName(
        [Description("Table name")] string tableName)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var columns = db.DbMaintenance.GetColumnInfosByTableName(tableName, false);
            return _databaseHelper.SerializeResult(new { success = true, data = columns });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Return all identity (auto-increment) columns for the provided tableName so callers know whether an identity key exists.")]
    public string GetIsIdentities(
        [Description("Table name")] string tableName)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var identities = db.DbMaintenance.GetIsIdentities(tableName);
            return _databaseHelper.SerializeResult(new { success = true, data = identities });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Return primary key metadata for the provided tableName, including the constraint name, columns, and ordinal order for composite keys.")]
    public string GetPrimaries(
        [Description("Table name")] string tableName)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var primaries = db.DbMaintenance.GetPrimaries(tableName);
            return _databaseHelper.SerializeResult(new { success = true, data = primaries });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    #endregion 数据库信息查询

    #region 存在性检查

    [McpServerTool]
    [Description("Check whether tableName exists in the current database and return an exists boolean.")]
    public string IsAnyTable(
        [Description("Table name")] string tableName)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var exists = db.DbMaintenance.IsAnyTable(tableName, false);
            return _databaseHelper.SerializeResult(new { success = true, exists });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Check whether columnName exists on tableName and return an exists boolean.")]
    public string IsAnyColumn(
        [Description("Table name")] string tableName,
        [Description("Column name")] string columnName)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var exists = db.DbMaintenance.IsAnyColumn(tableName, columnName);
            return _databaseHelper.SerializeResult(new { success = true, exists });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Check whether the specified constraintName exists (unique, foreign key, or check constraint) and return an exists boolean.")]
    public string IsAnyConstraint(
        [Description("Constraint name")] string constraintName)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var exists = db.DbMaintenance.IsAnyConstraint(constraintName);
            return _databaseHelper.SerializeResult(new { success = true, exists });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    #endregion 存在性检查

    #region 表操作

    [McpServerTool]
    [Description("Immediately drop the specified tableName, removing both structure and data, and return success to indicate completion.")]
    public string DropTable(
        [Description("Table name")] string tableName)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var result = db.DbMaintenance.DropTable(tableName);
            return _databaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Execute TRUNCATE on tableName to delete all rows while preserving the schema; the response includes success.")]
    public string TruncateTable(
        [Description("Table name")] string tableName)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var result = db.DbMaintenance.TruncateTable(tableName);
            return _databaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Use DbMaintenance.BackupTable to copy oldTableName to newTableName, duplicating both schema and current data.")]
    public string BackupTable(
        [Description("Original table name")] string oldTableName,
        [Description("New table name")] string newTableName)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var result = db.DbMaintenance.BackupTable(oldTableName, newTableName);
            return _databaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Rename oldTableName to newTableName and return success so callers know the rename succeeded.")]
    public string RenameTable(
        [Description("Original table name")] string oldTableName,
        [Description("New table name")] string newTableName)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var result = db.DbMaintenance.RenameTable(oldTableName, newTableName);
            return _databaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    #endregion 表操作

    #region 列操作

    [McpServerTool]
    [Description("Add a column on tableName using columnInfo JSON (DbColumnName, DataType, Length, IsNullable, DecimalDigits, etc.) to describe the new definition.")]
    public string AddColumn(
        [Description("Table name")] string tableName,
        [Description("Column info JSON with properties like DbColumnName, DataType, Length, IsNullable, DecimalDigits")] string columnInfo)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var columnData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(columnInfo);
            if (columnData == null)
                throw new ArgumentException("无效的列信息 JSON");

            var dataType = GetStringValue(columnData, "DataType") ?? "varchar";
            var column = new DbColumnInfo
            {
                DbColumnName = GetStringValue(columnData, "DbColumnName") ?? "",
                DataType = dataType,
                IsNullable = GetBoolValue(columnData, "IsNullable", true)
            };

            // 根据数据类型智能处理 Length 和 DecimalDigits
            if (RequiresLength(dataType))
            {
                column.Length = GetIntValue(columnData, "Length", GetDefaultLength(dataType));
            }

            if (RequiresDecimalDigits(dataType))
            {
                column.Length = GetIntValue(columnData, "Length", 18); // Precision
                column.DecimalDigits = GetIntValue(columnData, "DecimalDigits", 2); // Scale
            }

            var result = db.DbMaintenance.AddColumn(tableName, column);
            return _databaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Modify an existing column on tableName using the supplied columnInfo JSON to specify DbColumnName, DataType, Length, IsNullable, DecimalDigits, and other properties.")]
    public string UpdateColumn(
        [Description("Table name")] string tableName,
        [Description("Column info JSON with properties like DbColumnName, DataType, Length, IsNullable, DecimalDigits")] string columnInfo)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var columnData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(columnInfo);
            if (columnData == null)
                throw new ArgumentException("无效的列信息 JSON");

            var dataType = GetStringValue(columnData, "DataType") ?? "varchar";
            var column = new DbColumnInfo
            {
                DbColumnName = GetStringValue(columnData, "DbColumnName") ?? "",
                DataType = dataType,
                IsNullable = GetBoolValue(columnData, "IsNullable", true)
            };

            // 根据数据类型智能处理 Length 和 DecimalDigits
            if (RequiresLength(dataType))
            {
                column.Length = GetIntValue(columnData, "Length", GetDefaultLength(dataType));
            }

            if (RequiresDecimalDigits(dataType))
            {
                column.Length = GetIntValue(columnData, "Length", 18); // Precision
                column.DecimalDigits = GetIntValue(columnData, "DecimalDigits", 2); // Scale
            }

            var result = db.DbMaintenance.UpdateColumn(tableName, column);
            return _databaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Drop the specified columnName from tableName and return success.")]
    public string DropColumn(
        [Description("Table name")] string tableName,
        [Description("Column name")] string columnName)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var result = db.DbMaintenance.DropColumn(tableName, columnName);
            return _databaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Rename oldColumnName to newColumnName within tableName to adjust field naming.")]
    public string RenameColumn(
        [Description("Table name")] string tableName,
        [Description("Original column name")] string oldColumnName,
        [Description("New column name")] string newColumnName)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var result = db.DbMaintenance.RenameColumn(tableName, oldColumnName, newColumnName);
            return _databaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    #endregion 列操作

    #region 约束和索引操作

    [McpServerTool]
    [Description("Create a primary key constraint on tableName for columnName, useful when introducing a single-column key.")]
    public string AddPrimaryKey(
        [Description("Table name")] string tableName,
        [Description("Column name")] string columnName)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var result = db.DbMaintenance.AddPrimaryKey(tableName, columnName);
            return _databaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Drop the specified constraintName from tableName (primary key, unique index, foreign key, etc.) and return success.")]
    public string DropConstraint(
        [Description("Table name")] string tableName,
        [Description("Constraint name")] string constraintName)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var result = db.DbMaintenance.DropConstraint(tableName, constraintName);
            return _databaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Create an index named indexName for tableName.columnName; set isUnique to true to build a unique index and return success.")]
    public string CreateIndex(
        [Description("Table name")] string tableName,
        [Description("Index name")] string indexName,
        [Description("Column name")] string columnName,
        [Description("Whether it's a unique index")] bool isUnique = false)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var result = db.DbMaintenance.CreateIndex(tableName, new string[] { columnName }, indexName, isUnique);
            return _databaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("List every index defined on tableName (name and attributes) and return it in the data array.")]
    public string GetIndexList(
        [Description("Table name")] string tableName)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var indexes = db.DbMaintenance.GetIndexList(tableName);
            return _databaseHelper.SerializeResult(new { success = true, data = indexes });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    #endregion 约束和索引操作

    #region 默认值和注释

    [McpServerTool]
    [Description("Set defaultValue on tableName.columnName by invoking DbMaintenance.AddDefaultValue.")]
    public string AddDefaultValue(
        [Description("Table name")] string tableName,
        [Description("Column name")] string columnName,
        [Description("Default value")] string defaultValue)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var result = db.DbMaintenance.AddDefaultValue(tableName, columnName, defaultValue);
            return _databaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Attach a description to tableName so documentation tools can surface the table’s purpose.")]
    public string AddTableRemark(
        [Description("Table name")] string tableName,
        [Description("Table description")] string description)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var result = db.DbMaintenance.AddTableRemark(tableName, description);
            return _databaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Return exists to show whether tableName already has a stored remark.")]
    public string IsAnyTableRemark(
        [Description("Table name")] string tableName)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var exists = db.DbMaintenance.IsAnyTableRemark(tableName);
            return _databaseHelper.SerializeResult(new { success = true, exists });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Remove the remark/description associated with tableName and return success.")]
    public string DeleteTableRemark(
        [Description("Table name")] string tableName)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var result = db.DbMaintenance.DeleteTableRemark(tableName);
            return _databaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Attach a description to tableName.columnName, making the column meaning visible to downstream tools.")]
    public string AddColumnRemark(
        [Description("Table name")] string tableName,
        [Description("Column name")] string columnName,
        [Description("Column description")] string description)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();

            // 针对 SQL Server，使用 sp_addextendedproperty 存储过程
            if (db.CurrentConnectionConfig.DbType == DbType.SqlServer)
            {
                // 先检查是否已存在描述
                var existsSql = @"SELECT COUNT(*)
                    FROM sys.extended_properties ep
                    INNER JOIN sys.tables t ON ep.major_id = t.object_id
                    INNER JOIN sys.columns c ON ep.major_id = c.object_id AND ep.minor_id = c.column_id
                    WHERE t.name = @TableName AND c.name = @ColumnName AND ep.name = 'MS_Description'";

                var exists = db.Ado.GetInt(existsSql, new SugarParameter("@TableName", tableName), new SugarParameter("@ColumnName", columnName)) > 0;

                if (exists)
                {
                    // 如果已存在，先删除
                    var dropSql = @"EXEC sys.sp_dropextendedproperty
                        @name=N'MS_Description',
                        @level0type=N'SCHEMA', @level0name=N'dbo',
                        @level1type=N'TABLE',  @level1name=@TableName,
                        @level2type=N'COLUMN', @level2name=@ColumnName";
                    db.Ado.ExecuteCommand(dropSql, new SugarParameter("@TableName", tableName), new SugarParameter("@ColumnName", columnName));
                }

                // 添加新的描述
                var addSql = @"EXEC sys.sp_addextendedproperty
                    @name=N'MS_Description',
                    @value=@Description,
                    @level0type=N'SCHEMA', @level0name=N'dbo',
                    @level1type=N'TABLE',  @level1name=@TableName,
                    @level2type=N'COLUMN', @level2name=@ColumnName";

                db.Ado.ExecuteCommand(addSql,
                    new SugarParameter("@TableName", tableName),
                    new SugarParameter("@ColumnName", columnName),
                    new SugarParameter("@Description", description));

                return _databaseHelper.SerializeResult(new { success = true });
            }
            else
            {
                // 其他数据库使用 SqlSugar 的方法
                var result = db.DbMaintenance.AddColumnRemark(columnName, tableName, description);
                return _databaseHelper.SerializeResult(new { success = result });
            }
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Delete the stored description for tableName.columnName and return success.")]
    public string DeleteColumnRemark(
        [Description("Table name")] string tableName,
        [Description("Column name")] string columnName)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();

            // 针对 SQL Server，使用 sp_dropextendedproperty 存储过程
            if (db.CurrentConnectionConfig.DbType == DbType.SqlServer)
            {
                var dropSql = @"EXEC sys.sp_dropextendedproperty
                    @name=N'MS_Description',
                    @level0type=N'SCHEMA', @level0name=N'dbo',
                    @level1type=N'TABLE',  @level1name=@TableName,
                    @level2type=N'COLUMN', @level2name=@ColumnName";

                db.Ado.ExecuteCommand(dropSql,
                    new SugarParameter("@TableName", tableName),
                    new SugarParameter("@ColumnName", columnName));

                return _databaseHelper.SerializeResult(new { success = true });
            }
            else
            {
                // 其他数据库使用 SqlSugar 的方法
                var result = db.DbMaintenance.DeleteColumnRemark(columnName, tableName);
                return _databaseHelper.SerializeResult(new { success = result });
            }
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    #endregion 默认值和注释

    #region 存储过程、函数、视图操作

    [McpServerTool]
    [Description("List every stored procedure name in the current database and place the collection in data.")]
    public string GetProcList()
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var procedures = db.DbMaintenance.GetProcList();
            return _databaseHelper.SerializeResult(new { success = true, data = procedures });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("List every database function name so callers can audit reusable logic.")]
    public string GetFuncList()
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var functions = db.DbMaintenance.GetFuncList();
            return _databaseHelper.SerializeResult(new { success = true, data = functions });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Drop the specified viewName definition and return success.")]
    public string DropView(
        [Description("View name")] string viewName)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var result = db.DbMaintenance.DropView(viewName);
            return _databaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Drop the database function identified by functionName, typically during cleanup.")]
    public string DropFunc(
        [Description("Function name")] string functionName)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var result = db.DbMaintenance.DropFunction(functionName);
            return _databaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Drop the stored procedure identified by procedureName and return success.")]
    public string DropProc(
        [Description("Stored procedure name")] string procedureName)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var result = db.DbMaintenance.DropProc(procedureName);
            return _databaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    #endregion 存储过程、函数、视图操作

    #region 其他工具

    [McpServerTool]
    [Description("List every trigger defined on tableName to expose side effects that may fire on DML.")]
    public string GetTriggerNames(
        [Description("Table name")] string tableName)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var triggers = db.DbMaintenance.GetTriggerNames(tableName);
            return _databaseHelper.SerializeResult(new { success = true, data = triggers });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Return a combined schema document for tableName including columns, primary keys, identity columns, and indexes.")]
    public string GetTableSchema(
        [Description("Table name")] string tableName)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var columns = db.DbMaintenance.GetColumnInfosByTableName(tableName);
            var primaries = db.DbMaintenance.GetPrimaries(tableName);
            var identities = db.DbMaintenance.GetIsIdentities(tableName);
            var indexes = db.DbMaintenance.GetIndexList(tableName);

            var schema = new
            {
                tableName,
                columns,
                primaryKeys = primaries,
                identityColumns = identities,
                indexes
            };

            return _databaseHelper.SerializeResult(new { success = true, data = schema });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    #endregion 其他工具

    #region 辅助方法

    /// <summary>
    /// 从 JsonElement 字典中安全地获取字符串值
    /// </summary>
    private static string? GetStringValue(Dictionary<string, JsonElement> dict, string key)
    {
        if (!dict.TryGetValue(key, out var element))
            return null;

        return element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
    }

    /// <summary>
    /// 从 JsonElement 字典中安全地获取整数值
    /// </summary>
    private static int GetIntValue(Dictionary<string, JsonElement> dict, string key, int defaultValue)
    {
        if (!dict.TryGetValue(key, out var element))
            return defaultValue;

        if (element.ValueKind == JsonValueKind.Number)
            return element.GetInt32();

        if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out var intValue))
            return intValue;

        return defaultValue;
    }

    /// <summary>
    /// 从 JsonElement 字典中安全地获取布尔值
    /// </summary>
    private static bool GetBoolValue(Dictionary<string, JsonElement> dict, string key, bool defaultValue)
    {
        if (!dict.TryGetValue(key, out var element))
            return defaultValue;

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(element.GetString(), out var boolValue) => boolValue,
            _ => defaultValue
        };
    }

    #endregion 辅助方法

    #region 数据类型处理辅助方法

    /// <summary>
    /// 判断数据类型是否需要 Length 参数
    /// </summary>
    private static bool RequiresLength(string dataType)
    {
        var type = dataType.ToLowerInvariant();

        // 字符串类型需要 Length
        string[] stringTypes = { "char", "varchar", "nchar", "nvarchar", "binary", "varbinary" };
        return stringTypes.Any(t => type.Contains(t));
    }

    /// <summary>
    /// 判断数据类型是否需要 DecimalDigits (精度和小数位数)
    /// </summary>
    private static bool RequiresDecimalDigits(string dataType)
    {
        var type = dataType.ToLowerInvariant();

        // decimal 和 numeric 类型需要精度和小数位数
        return type.Contains("decimal") || type.Contains("numeric");
    }

    /// <summary>
    /// 获取数据类型的默认长度
    /// </summary>
    private static int GetDefaultLength(string dataType)
    {
        var type = dataType.ToLowerInvariant();

        return type switch
        {
            // 固定长度字符类型
            var t when t.Contains("nchar") && !t.Contains("nvarchar") => 10,
            var t when t.Contains("char") && !t.Contains("varchar") && !t.Contains("nchar") => 10,

            // 可变长度字符类型
            var t when t.Contains("nvarchar") => 50,
            var t when t.Contains("varchar") => 50,

            // 二进制类型
            var t when t.Contains("varbinary") => 50,
            var t when t.Contains("binary") && !t.Contains("varbinary") => 8,

            // 默认
            _ => 255
        };
    }

    #endregion 数据类型处理辅助方法
}
