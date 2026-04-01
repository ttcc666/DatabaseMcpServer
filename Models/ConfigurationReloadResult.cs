namespace DatabaseMcpServer.Models;

/// <summary>
/// 数据库配置刷新结果
/// </summary>
public class ConfigurationReloadResult
{
    /// <summary>
    /// 是否刷新成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 刷新结果消息
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 配置文件路径
    /// </summary>
    public string ConfigPath { get; set; } = string.Empty;

    /// <summary>
    /// 刷新前的当前数据库
    /// </summary>
    public string PreviousDatabase { get; set; } = string.Empty;

    /// <summary>
    /// 刷新后的当前数据库
    /// </summary>
    public string CurrentDatabase { get; set; } = string.Empty;

    /// <summary>
    /// 刷新后可用数据库总数
    /// </summary>
    public int TotalDatabases { get; set; }

    /// <summary>
    /// 是否保留了原有当前数据库
    /// </summary>
    public bool PreservedCurrentDatabase { get; set; }
}
