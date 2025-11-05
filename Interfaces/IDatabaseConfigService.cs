using SqlSugar;

namespace DatabaseMcpServer.Interfaces;

/// <summary>
/// 数据库配置服务接口
/// </summary>
public interface IDatabaseConfigService
{
    /// <summary>
    /// 创建数据库客户端实例
    /// </summary>
    /// <returns>配置好的 SqlSugarClient 实例</returns>
    SqlSugarClient CreateClient();

    /// <summary>
    /// 异步创建数据库客户端实例
    /// </summary>
    /// <returns>配置好的 SqlSugarClient 实例</returns>
    Task<SqlSugarClient> CreateClientAsync();

    /// <summary>
    /// 获取数据库连接字符串
    /// </summary>
    /// <returns>连接字符串</returns>
    string GetConnectionString();

    /// <summary>
    /// 获取数据库类型
    /// </summary>
    /// <returns>数据库类型字符串</returns>
    string GetDatabaseType();

    /// <summary>
    /// 获取解析后的数据库类型
    /// </summary>
    /// <returns>SqlSugar DbType 枚举</returns>
    DbType GetParsedDbType();

    /// <summary>
    /// 验证数据库配置是否正确
    /// </summary>
    /// <returns>配置是否有效</returns>
    bool ValidateConfiguration();

    /// <summary>
    /// 获取配置信息摘要
    /// </summary>
    /// <returns>配置信息 JSON 字符串</returns>
    string GetConfigurationSummary();
}