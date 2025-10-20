using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Services;
using SqlSugar;

namespace DatabaseMcpServer.Tools;

/// <summary>
/// 数据库架构管理工具类，用于执行数据库架构相关操作。
/// </summary>
internal class DatabaseSchemaTools
{
    #region 数据库信息查询

    /// <summary>
    /// 获取所有数据库名称。
    /// </summary>
    /// <returns>包含数据库列表的 JSON 字符串</returns>
    [McpServerTool]
    [Description("获取所有数据库名称")]
    public string GetDataBaseList()
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var databases = db.DbMaintenance.GetDataBaseList();
            return DatabaseHelper.SerializeResult(new { success = true, data = databases });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 查询所有视图。
    /// </summary>
    /// <returns>包含视图列表的 JSON 字符串</returns>
    [McpServerTool]
    [Description("查询所有视图")]
    public string GetViewInfoList()
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var views = db.DbMaintenance.GetViewInfoList();
            return DatabaseHelper.SerializeResult(new { success = true, data = views });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 获取所有表名。
    /// </summary>
    /// <returns>包含表列表的 JSON 字符串</returns>
    [McpServerTool]
    [Description("获取所有表名")]
    public string GetTableInfoList()
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var tables = db.DbMaintenance.GetTableInfoList(false);
            return DatabaseHelper.SerializeResult(new { success = true, data = tables });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 根据表名获取字段信息。
    /// </summary>
    /// <param name="tableName">表名</param>
    /// <returns>包含字段信息列表的 JSON 字符串</returns>
    [McpServerTool]
    [Description("根据表名获取字段信息")]
    public string GetColumnInfosByTableName(
        [Description("表名")] string tableName)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var columns = db.DbMaintenance.GetColumnInfosByTableName(tableName, false);
            return DatabaseHelper.SerializeResult(new { success = true, data = columns });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 获取自增列。
    /// </summary>
    /// <param name="tableName">表名</param>
    /// <returns>包含自增列列表的 JSON 字符串</returns>
    [McpServerTool]
    [Description("获取自增列")]
    public string GetIsIdentities(
        [Description("表名")] string tableName)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var identities = db.DbMaintenance.GetIsIdentities(tableName);
            return DatabaseHelper.SerializeResult(new { success = true, data = identities });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 获取主键。
    /// </summary>
    /// <param name="tableName">表名</param>
    /// <returns>包含主键列表的 JSON 字符串</returns>
    [McpServerTool]
    [Description("获取主键")]
    public string GetPrimaries(
        [Description("表名")] string tableName)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var primaries = db.DbMaintenance.GetPrimaries(tableName);
            return DatabaseHelper.SerializeResult(new { success = true, data = primaries });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    #endregion

    #region 存在性检查

    /// <summary>
    /// 判断表是否存在。
    /// </summary>
    /// <param name="tableName">表名</param>
    /// <returns>包含检查结果的 JSON 字符串</returns>
    [McpServerTool]
    [Description("判断表是否存在")]
    public string IsAnyTable(
        [Description("表名")] string tableName)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var exists = db.DbMaintenance.IsAnyTable(tableName, false);
            return DatabaseHelper.SerializeResult(new { success = true, exists });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 判断列是否存在。
    /// </summary>
    /// <param name="tableName">表名</param>
    /// <param name="columnName">列名</param>
    /// <returns>包含检查结果的 JSON 字符串</returns>
    [McpServerTool]
    [Description("判断列是否存在")]
    public string IsAnyColumn(
        [Description("表名")] string tableName,
        [Description("列名")] string columnName)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var exists = db.DbMaintenance.IsAnyColumn(tableName, columnName);
            return DatabaseHelper.SerializeResult(new { success = true, exists });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 判断主键是否存在。
    /// </summary>
    /// <param name="tableName">表名</param>
    /// <param name="columnName">列名</param>
    /// <returns>包含检查结果的 JSON 字符串</returns>
    [McpServerTool]
    [Description("判断主键是否存在")]
    public string IsPrimaryKey(
        [Description("表名")] string tableName,
        [Description("列名")] string columnName)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var isPrimary = db.DbMaintenance.IsPrimaryKey(tableName, columnName);
            return DatabaseHelper.SerializeResult(new { success = true, isPrimary });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 判断自增是否存在。
    /// </summary>
    /// <param name="tableName">表名</param>
    /// <param name="columnName">列名</param>
    /// <returns>包含检查结果的 JSON 字符串</returns>
    [McpServerTool]
    [Description("判断自增是否存在")]
    public string IsIdentity(
        [Description("表名")] string tableName,
        [Description("列名")] string columnName)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var isIdentity = db.DbMaintenance.IsIdentity(tableName, columnName);
            return DatabaseHelper.SerializeResult(new { success = true, isIdentity });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 判断约束是否存在。
    /// </summary>
    /// <param name="constraintName">约束名</param>
    /// <returns>包含检查结果的 JSON 字符串</returns>
    [McpServerTool]
    [Description("判断约束是否存在")]
    public string IsAnyConstraint(
        [Description("约束名")] string constraintName)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var exists = db.DbMaintenance.IsAnyConstraint(constraintName);
            return DatabaseHelper.SerializeResult(new { success = true, exists });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    #endregion

    #region 表操作

    /// <summary>
    /// 删除表。
    /// </summary>
    /// <param name="tableName">表名</param>
    /// <returns>包含操作结果的 JSON 字符串</returns>
    [McpServerTool]
    [Description("删除表")]
    public string DropTable(
        [Description("表名")] string tableName)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var result = db.DbMaintenance.DropTable(tableName);
            return DatabaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 清空表。
    /// </summary>
    /// <param name="tableName">表名</param>
    /// <returns>包含操作结果的 JSON 字符串</returns>
    [McpServerTool]
    [Description("清空表")]
    public string TruncateTable(
        [Description("表名")] string tableName)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var result = db.DbMaintenance.TruncateTable(tableName);
            return DatabaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 备份表。
    /// </summary>
    /// <param name="oldTableName">原表名</param>
    /// <param name="newTableName">新表名</param>
    /// <returns>包含操作结果的 JSON 字符串</returns>
    [McpServerTool]
    [Description("备份表")]
    public string BackupTable(
        [Description("原表名")] string oldTableName,
        [Description("新表名")] string newTableName)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var result = db.DbMaintenance.BackupTable(oldTableName, newTableName);
            return DatabaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 重命名表。
    /// </summary>
    /// <param name="oldTableName">原表名</param>
    /// <param name="newTableName">新表名</param>
    /// <returns>包含操作结果的 JSON 字符串</returns>
    [McpServerTool]
    [Description("重命名表")]
    public string RenameTable(
        [Description("原表名")] string oldTableName,
        [Description("新表名")] string newTableName)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var result = db.DbMaintenance.RenameTable(oldTableName, newTableName);
            return DatabaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    #endregion

    #region 列操作

    /// <summary>
    /// 添加列。
    /// </summary>
    /// <param name="tableName">表名</param>
    /// <param name="columnInfo">列信息 JSON</param>
    /// <returns>包含操作结果的 JSON 字符串</returns>
    [McpServerTool]
    [Description("添加列")]
    public string AddColumn(
        [Description("表名")] string tableName,
        [Description("列信息 JSON，包含 DbColumnName, DataType, Length, IsNullable 等属性")] string columnInfo)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
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
            return DatabaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 更新列。
    /// </summary>
    /// <param name="tableName">表名</param>
    /// <param name="columnInfo">列信息 JSON</param>
    /// <returns>包含操作结果的 JSON 字符串</returns>
    [McpServerTool]
    [Description("更新列")]
    public string UpdateColumn(
        [Description("表名")] string tableName,
        [Description("列信息 JSON，包含 DbColumnName, DataType, Length, IsNullable 等属性")] string columnInfo)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
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
            return DatabaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 删除列。
    /// </summary>
    /// <param name="tableName">表名</param>
    /// <param name="columnName">列名</param>
    /// <returns>包含操作结果的 JSON 字符串</returns>
    [McpServerTool]
    [Description("删除列")]
    public string DropColumn(
        [Description("表名")] string tableName,
        [Description("列名")] string columnName)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var result = db.DbMaintenance.DropColumn(tableName, columnName);
            return DatabaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 重命名列。
    /// </summary>
    /// <param name="tableName">表名</param>
    /// <param name="oldColumnName">原列名</param>
    /// <param name="newColumnName">新列名</param>
    /// <returns>包含操作结果的 JSON 字符串</returns>
    [McpServerTool]
    [Description("重命名列")]
    public string RenameColumn(
        [Description("表名")] string tableName,
        [Description("原列名")] string oldColumnName,
        [Description("新列名")] string newColumnName)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var result = db.DbMaintenance.RenameColumn(tableName, oldColumnName, newColumnName);
            return DatabaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    #endregion

    #region 约束和索引操作

    /// <summary>
    /// 添加主键。
    /// </summary>
    /// <param name="tableName">表名</param>
    /// <param name="columnName">列名</param>
    /// <returns>包含操作结果的 JSON 字符串</returns>
    [McpServerTool]
    [Description("添加主键")]
    public string AddPrimaryKey(
        [Description("表名")] string tableName,
        [Description("列名")] string columnName)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var result = db.DbMaintenance.AddPrimaryKey(tableName, columnName);
            return DatabaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 删除约束。
    /// </summary>
    /// <param name="tableName">表名</param>
    /// <param name="constraintName">约束名</param>
    /// <returns>包含操作结果的 JSON 字符串</returns>
    [McpServerTool]
    [Description("删除约束")]
    public string DropConstraint(
        [Description("表名")] string tableName,
        [Description("约束名")] string constraintName)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var result = db.DbMaintenance.DropConstraint(tableName, constraintName);
            return DatabaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 创建索引或唯一约束。
    /// </summary>
    /// <param name="tableName">表名</param>
    /// <param name="indexName">索引名</param>
    /// <param name="columnName">列名</param>
    /// <param name="isUnique">是否唯一索引</param>
    /// <returns>包含操作结果的 JSON 字符串</returns>
    [McpServerTool]
    [Description("创建索引或唯一约束")]
    public string CreateIndex(
        [Description("表名")] string tableName,
        [Description("索引名")] string indexName,
        [Description("列名")] string columnName,
        [Description("是否唯一索引")] bool isUnique = false)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var result = db.DbMaintenance.CreateIndex(tableName, new string[] { columnName }, indexName, isUnique);
            return DatabaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 判断索引是否存在。
    /// </summary>
    /// <param name="indexName">索引名</param>
    /// <returns>包含检查结果的 JSON 字符串</returns>
    [McpServerTool]
    [Description("判断索引是否存在")]
    public string IsAnyIndex(
        [Description("索引名")] string indexName)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var exists = db.DbMaintenance.IsAnyIndex(indexName);
            return DatabaseHelper.SerializeResult(new { success = true, exists });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 获取所有索引名字集合。
    /// </summary>
    /// <param name="tableName">表名</param>
    /// <returns>包含索引列表的 JSON 字符串</returns>
    [McpServerTool]
    [Description("获取所有索引名字集合")]
    public string GetIndexList(
        [Description("表名")] string tableName)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var indexes = db.DbMaintenance.GetIndexList(tableName);
            return DatabaseHelper.SerializeResult(new { success = true, data = indexes });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    #endregion

    #region 默认值和注释

    /// <summary>
    /// 添加默认值。
    /// </summary>
    /// <param name="tableName">表名</param>
    /// <param name="columnName">列名</param>
    /// <param name="defaultValue">默认值</param>
    /// <returns>包含操作结果的 JSON 字符串</returns>
    [McpServerTool]
    [Description("添加默认值")]
    public string AddDefaultValue(
        [Description("表名")] string tableName,
        [Description("列名")] string columnName,
        [Description("默认值")] string defaultValue)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var result = db.DbMaintenance.AddDefaultValue(tableName, columnName, defaultValue);
            return DatabaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 添加表描述。
    /// </summary>
    /// <param name="tableName">表名</param>
    /// <param name="description">表描述</param>
    /// <returns>包含操作结果的 JSON 字符串</returns>
    [McpServerTool]
    [Description("添加表描述")]
    public string AddTableRemark(
        [Description("表名")] string tableName,
        [Description("表描述")] string description)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var result = db.DbMaintenance.AddTableRemark(tableName, description);
            return DatabaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 判断是否存在表描述。
    /// </summary>
    /// <param name="tableName">表名</param>
    /// <returns>包含检查结果的 JSON 字符串</returns>
    [McpServerTool]
    [Description("判断是否存在表描述")]
    public string IsAnyTableRemark(
        [Description("表名")] string tableName)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var exists = db.DbMaintenance.IsAnyTableRemark(tableName);
            return DatabaseHelper.SerializeResult(new { success = true, exists });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 删除表描述。
    /// </summary>
    /// <param name="tableName">表名</param>
    /// <returns>包含操作结果的 JSON 字符串</returns>
    [McpServerTool]
    [Description("删除表描述")]
    public string DeleteTableRemark(
        [Description("表名")] string tableName)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var result = db.DbMaintenance.DeleteTableRemark(tableName);
            return DatabaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 添加列描述。
    /// </summary>
    /// <param name="tableName">表名</param>
    /// <param name="columnName">列名</param>
    /// <param name="description">列描述</param>
    /// <returns>包含操作结果的 JSON 字符串</returns>
    [McpServerTool]
    [Description("添加列描述")]
    public string AddColumnRemark(
        [Description("表名")] string tableName,
        [Description("列名")] string columnName,
        [Description("列描述")] string description)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var result = db.DbMaintenance.AddColumnRemark(tableName, columnName, description);
            return DatabaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 删除列描述。
    /// </summary>
    /// <param name="tableName">表名</param>
    /// <param name="columnName">列名</param>
    /// <returns>包含操作结果的 JSON 字符串</returns>
    [McpServerTool]
    [Description("删除列描述")]
    public string DeleteColumnRemark(
        [Description("表名")] string tableName,
        [Description("列名")] string columnName)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var result = db.DbMaintenance.DeleteColumnRemark(tableName, columnName);
            return DatabaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    #endregion

    #region 存储过程、函数、视图操作

    /// <summary>
    /// 获取存储过程名字集合。
    /// </summary>
    /// <returns>包含存储过程列表的 JSON 字符串</returns>
    [McpServerTool]
    [Description("获取存储过程名字集合")]
    public string GetProcList()
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var procedures = db.DbMaintenance.GetProcList();
            return DatabaseHelper.SerializeResult(new { success = true, data = procedures });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 获取函数集合。
    /// </summary>
    /// <returns>包含函数列表的 JSON 字符串</returns>
    [McpServerTool]
    [Description("获取函数集合")]
    public string GetFuncList()
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var functions = db.DbMaintenance.GetFuncList();
            return DatabaseHelper.SerializeResult(new { success = true, data = functions });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 删除视图。
    /// </summary>
    /// <param name="viewName">视图名</param>
    /// <returns>包含操作结果的 JSON 字符串</returns>
    [McpServerTool]
    [Description("删除视图")]
    public string DropView(
        [Description("视图名")] string viewName)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var result = db.DbMaintenance.DropView(viewName);
            return DatabaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 删除函数。
    /// </summary>
    /// <param name="functionName">函数名</param>
    /// <returns>包含操作结果的 JSON 字符串</returns>
    [McpServerTool]
    [Description("删除函数")]
    public string DropFunc(
        [Description("函数名")] string functionName)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var result = db.DbMaintenance.DropFunction(functionName);
            return DatabaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 删除存储过程。
    /// </summary>
    /// <param name="procedureName">存储过程名</param>
    /// <returns>包含操作结果的 JSON 字符串</returns>
    [McpServerTool]
    [Description("删除存储过程")]
    public string DropProc(
        [Description("存储过程名")] string procedureName)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var result = db.DbMaintenance.DropProc(procedureName);
            return DatabaseHelper.SerializeResult(new { success = result });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    #endregion

    #region 其他工具

    /// <summary>
    /// 获取数据库类型集合。
    /// </summary>
    /// <returns>包含数据库类型列表的 JSON 字符串</returns>
    [McpServerTool]
    [Description("获取数据库类型集合")]
    public string GetDbTypes()
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var dbTypes = db.DbMaintenance.GetDbTypes();
            return DatabaseHelper.SerializeResult(new { success = true, data = dbTypes });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 根据表名获取触发器集合。
    /// </summary>
    /// <param name="tableName">表名</param>
    /// <returns>包含触发器列表的 JSON 字符串</returns>
    [McpServerTool]
    [Description("根据表名获取触发器集合")]
    public string GetTriggerNames(
        [Description("表名")] string tableName)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
            var triggers = db.DbMaintenance.GetTriggerNames(tableName);
            return DatabaseHelper.SerializeResult(new { success = true, data = triggers });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 获取表结构信息。
    /// </summary>
    /// <param name="tableName">表名</param>
    /// <returns>包含表结构信息的 JSON 字符串</returns>
    [McpServerTool]
    [Description("获取表结构信息")]
    public string GetTableSchema(
        [Description("表名")] string tableName)
    {
        try
        {
            using var db = DatabaseConfigService.CreateGlobalClient();
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

            return DatabaseHelper.SerializeResult(new { success = true, data = schema });
        }
        catch (Exception ex)
        {
            return DatabaseHelper.SerializeResult(new { success = false, error = ex.Message });
        }
    }

    #endregion
}
