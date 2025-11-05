using SqlSugar;

namespace DatabaseMcpServer.Interfaces;

/// <summary>
/// 数据库辅助服务接口
/// </summary>
public interface IDatabaseHelperService
{
    /// <summary>
    /// 将字符串类型转换为 SqlSugar 的 DbType 枚举
    /// </summary>
    /// <param name="dbType">数据库类型字符串</param>
    /// <returns>对应的 DbType 枚举值</returns>
    DbType ParseDbType(string dbType);

    /// <summary>
    /// 解析 JSON 格式的参数字符串为 SqlSugar 参数数组
    /// </summary>
    /// <param name="parametersJson">JSON 格式的参数字符串</param>
    /// <returns>SqlSugar 参数数组</returns>
    SugarParameter[]? ParseParameters(string? parametersJson);

    /// <summary>
    /// 将对象序列化为格式化的 JSON 字符串
    /// </summary>
    /// <param name="data">要序列化的数据对象</param>
    /// <returns>格式化的 JSON 字符串</returns>
    string SerializeResult(object data);

    /// <summary>
    /// 创建 SqlSugar 数据库客户端实例
    /// </summary>
    /// <param name="connectionString">数据库连接字符串</param>
    /// <param name="dbType">数据库类型</param>
    /// <returns>配置好的 SqlSugarClient 实例</returns>
    SqlSugarClient CreateClient(string connectionString, DbType dbType);

    /// <summary>
    /// 检测 SQL 语句中是否包含危险操作
    /// </summary>
    /// <param name="sql">要检测的 SQL 语句</param>
    /// <returns>如果包含危险操作返回 true</returns>
    bool DetectDangerousOperation(string sql);
}