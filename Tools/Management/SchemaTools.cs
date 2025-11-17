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
    [Description("Get all database names")]
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
    [Description("Query all views")]
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
    [Description("Get all table names")]
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
    [Description("Get column information by table name")]
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
    [Description("Get identity columns")]
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
    [Description("Get primary keys")]
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
    [Description("Check if table exists")]
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
    [Description("Check if column exists")]
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
    [Description("Check if primary key exists")]
    public string IsPrimaryKey(
        [Description("Table name")] string tableName,
        [Description("Column name")] string columnName)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var isPrimary = db.DbMaintenance.IsPrimaryKey(tableName, columnName);
            return _databaseHelper.SerializeResult(new { success = true, isPrimary });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Check if identity exists")]
    public string IsIdentity(
        [Description("Table name")] string tableName,
        [Description("Column name")] string columnName)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var isIdentity = db.DbMaintenance.IsIdentity(tableName, columnName);
            return _databaseHelper.SerializeResult(new { success = true, isIdentity });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Check if constraint exists")]
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
    [Description("Drop table")]
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
    [Description("Truncate table")]
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
    [Description("Backup table")]
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
    [Description("Rename table")]
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
    [Description("Add column")]
    public string AddColumn(
        [Description("Table name")] string tableName,
        [Description("Column info JSON with properties like DbColumnName, DataType, Length, IsNullable")] string columnInfo)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var columnData = JsonSerializer.Deserialize<Dictionary<string, object>>(columnInfo);
            if (columnData == null)
                throw new ArgumentException("无效的列信息 JSON");

            var column = new DbColumnInfo
            {
                DbColumnName = columnData.GetValueOrDefault("DbColumnName")?.ToString() ?? "",
                DataType = columnData.GetValueOrDefault("DataType")?.ToString() ?? "varchar",
                Length = Convert.ToInt32(columnData.GetValueOrDefault("Length") ?? 255),
                IsNullable = Convert.ToBoolean(columnData.GetValueOrDefault("IsNullable") ?? true)
            };

            var result = db.DbMaintenance.AddColumn(tableName, column);
            return _databaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Update column")]
    public string UpdateColumn(
        [Description("Table name")] string tableName,
        [Description("Column info JSON with properties like DbColumnName, DataType, Length, IsNullable")] string columnInfo)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var columnData = JsonSerializer.Deserialize<Dictionary<string, object>>(columnInfo);
            if (columnData == null)
                throw new ArgumentException("无效的列信息 JSON");

            var column = new DbColumnInfo
            {
                DbColumnName = columnData.GetValueOrDefault("DbColumnName")?.ToString() ?? "",
                DataType = columnData.GetValueOrDefault("DataType")?.ToString() ?? "varchar",
                Length = Convert.ToInt32(columnData.GetValueOrDefault("Length") ?? 255),
                IsNullable = Convert.ToBoolean(columnData.GetValueOrDefault("IsNullable") ?? true)
            };

            var result = db.DbMaintenance.UpdateColumn(tableName, column);
            return _databaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Drop column")]
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
    [Description("Rename column")]
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
    [Description("Add primary key")]
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
    [Description("Drop constraint")]
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
    [Description("Create index or unique constraint")]
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
    [Description("Check if index exists")]
    public string IsAnyIndex(
        [Description("Index name")] string indexName)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var exists = db.DbMaintenance.IsAnyIndex(indexName);
            return _databaseHelper.SerializeResult(new { success = true, exists });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Get all index names")]
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
    [Description("Add default value")]
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
    [Description("Add table description")]
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
    [Description("Check if table description exists")]
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
    [Description("Delete table description")]
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
    [Description("Add column description")]
    public string AddColumnRemark(
        [Description("Table name")] string tableName,
        [Description("Column name")] string columnName,
        [Description("Column description")] string description)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var result = db.DbMaintenance.AddColumnRemark(tableName, columnName, description);
            return _databaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Delete column description")]
    public string DeleteColumnRemark(
        [Description("Table name")] string tableName,
        [Description("Column name")] string columnName)
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var result = db.DbMaintenance.DeleteColumnRemark(tableName, columnName);
            return _databaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    #endregion 默认值和注释

    #region 存储过程、函数、视图操作

    [McpServerTool]
    [Description("Get stored procedure names")]
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
    [Description("Get function names")]
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
    [Description("Drop view")]
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
    [Description("Drop function")]
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
    [Description("Drop stored procedure")]
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
    [Description("Get database types")]
    public string GetDbTypes()
    {
        try
        {
            using var db = _databaseConfig.CreateClient();
            var dbTypes = db.DbMaintenance.GetDbTypes();
            return _databaseHelper.SerializeResult(new { success = true, data = dbTypes });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    [McpServerTool]
    [Description("Get trigger names by table name")]
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
    [Description("Get table schema information")]
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
}