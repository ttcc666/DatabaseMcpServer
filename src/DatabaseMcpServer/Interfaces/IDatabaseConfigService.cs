using DatabaseMcpServer.Models;
using SqlSugar;

namespace DatabaseMcpServer.Interfaces;

/// <summary>
/// 数据库配置服务接口
/// </summary>
public interface IDatabaseConfigService
{
    /// <summary>
    /// 获取数据库客户端实例（使用当前活动连接）。
    /// 返回由 SqlSugarScope 连接池复用的共享实例，调用方不应释放该实例。
    /// </summary>
    /// <returns>配置好的 ISqlSugarClient 实例</returns>
    ISqlSugarClient CreateClient();

    /// <summary>
    /// 获取指定数据库的客户端实例。
    /// 返回由 SqlSugarScope 连接池复用的共享实例，调用方不应释放该实例。
    /// </summary>
    /// <param name="databaseName">数据库连接名称</param>
    /// <returns>配置好的 ISqlSugarClient 实例</returns>
    ISqlSugarClient CreateClient(string databaseName);

    /// <summary>
    /// 获取当前数据库客户端及其连接级执行策略的原子快照。
    /// </summary>
    DatabaseClientContext CreateClientContext();

    /// <summary>
    /// 异步获取数据库客户端实例（使用当前活动连接）。
    /// </summary>
    /// <returns>配置好的 ISqlSugarClient 实例</returns>
    Task<ISqlSugarClient> CreateClientAsync();

    /// <summary>
    /// 获取数据库连接字符串（当前活动连接）
    /// </summary>
    /// <returns>连接字符串</returns>
    string GetConnectionString();

    /// <summary>
    /// 获取数据库类型（当前活动连接）
    /// </summary>
    /// <returns>数据库类型字符串</returns>
    string GetDatabaseType();

    /// <summary>
    /// 获取解析后的数据库类型（当前活动连接）
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

    /// <summary>
    /// 获取所有可用的数据库连接列表
    /// </summary>
    /// <returns>数据库连接列表</returns>
    List<DatabaseConnection> GetAllConnections();

    /// <summary>
    /// 切换当前活动的数据库连接
    /// </summary>
    /// <param name="databaseName">数据库连接名称</param>
    /// <returns>切换是否成功</returns>
    bool SwitchDatabase(string databaseName);

    /// <summary>
    /// 获取当前活动的数据库连接名称
    /// </summary>
    /// <returns>当前数据库连接名称</returns>
    string GetCurrentDatabaseName();

    /// <summary>
    /// 重新加载数据库配置文件并刷新客户端缓存
    /// </summary>
    /// <returns>刷新结果</returns>
    ConfigurationReloadResult ReloadConfiguration();
}
