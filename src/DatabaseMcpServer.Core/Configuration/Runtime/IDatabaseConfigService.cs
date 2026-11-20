using DatabaseMcpServer.Models;

namespace DatabaseMcpServer.Interfaces;

/// <summary>
/// 数据库配置服务接口
/// </summary>
public interface IDatabaseConfigService
{
    DatabaseOperationReservation ReserveConnection(string? databaseName = null);
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
    /// 验证数据库配置是否正确
    /// </summary>
    /// <returns>配置是否有效</returns>
    bool ValidateConfiguration();

    /// <summary>
    /// 获取配置信息摘要
    /// </summary>
    /// <returns>已脱敏的配置信息摘要</returns>
    ConfigurationSummary GetConfigurationSummary();

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
    /// 重新加载数据库配置文件并更新连接准入状态。
    /// 默认保留仍存在的当前库；文件监听应使用 <paramref name="followFileDefault"/> = true，
    /// 以便在 JSON 中的默认库变化时切换运行时当前库。
    /// </summary>
    /// <returns>刷新结果</returns>
    ConfigurationReloadResult ReloadConfiguration(bool followFileDefault = false);

    ConfigurationMutationResult RenameDatabase(string name, string newName);

    ConfigurationMutationResult UpdateDatabase(string name, DatabaseConnectionUpdate update);

    DatabaseRuntimeStatus GetRuntimeStatus();

    /// <summary>
    /// 当前已加载的配置文件绝对路径。
    /// </summary>
    string GetConfigFilePath();

    /// <summary>
    /// 当前是否应应用文件监听。
    /// 优先级：启动参数 --enable-monitor-config > 环境变量 ENABLE_MONITOR_CONFIG > 配置文件 enableMonitorConfig。
    /// </summary>
    bool IsEnableMonitorConfigEnabled();
}
