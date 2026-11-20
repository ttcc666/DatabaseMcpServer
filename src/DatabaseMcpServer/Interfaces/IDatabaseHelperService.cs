using SqlSugar;
using System.Data;
using DbType = SqlSugar.DbType;

namespace DatabaseMcpServer.Interfaces;

/// <summary>
/// 数据库辅助服务接口
/// </summary>
public interface IDatabaseHelperService
{
    /// <summary>
    /// 将字符串类型转换为 SqlSugar 的 DbType 枚举
    /// </summary>
    DbType ParseDbType(string dbType);

    /// <summary>
    /// 检测 SQL 语句中是否包含危险操作
    /// </summary>
    bool DetectDangerousOperation(string sql);

    /// <summary>
    /// 将 DataTable 转换为字典集合，便于返回 JSON
    /// </summary>
    List<Dictionary<string, object?>> ConvertDataTableToList(DataTable dataTable);
}
