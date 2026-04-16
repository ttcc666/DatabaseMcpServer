namespace DatabaseMcpServer.Interfaces;

/// <summary>
/// 持久化 CLI 模式下“当前数据库连接”的状态。
/// </summary>
public interface ICurrentDatabaseStateStore
{
    /// <summary>
    /// 读取指定配置文件对应的当前数据库名称。
    /// </summary>
    /// <param name="configPath">已解析的配置文件路径。</param>
    /// <returns>已保存的数据库名称；不存在时返回 null。</returns>
    string? GetCurrentDatabaseName(string configPath);

    /// <summary>
    /// 保存指定配置文件对应的当前数据库名称。
    /// </summary>
    /// <param name="configPath">已解析的配置文件路径。</param>
    /// <param name="databaseName">当前数据库名称。</param>
    void SaveCurrentDatabaseName(string configPath, string databaseName);
}
