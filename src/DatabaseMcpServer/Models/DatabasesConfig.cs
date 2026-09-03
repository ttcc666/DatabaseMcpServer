using System.Text.Json.Serialization;

namespace DatabaseMcpServer.Models;

/// <summary>
/// 数据库配置文件模型
/// </summary>
public class DatabasesConfig
{
    /// <summary>
    /// 是否监听本文件变化并让长驻进程（MCP stdio / -web）跟随新的默认库。
    /// 优先级：启动参数 --enable-monitor-config > 环境变量 ENABLE_MONITOR_CONFIG > 本字段。默认 false。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool EnableMonitorConfig { get; set; }

    /// <summary>
    /// 数据库连接列表
    /// </summary>
    public List<DatabaseConnection> Databases { get; set; } = [];
}
