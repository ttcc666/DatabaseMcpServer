namespace DatabaseMcpServer.Models;

/// <summary>
/// 数据库配置文件模型
/// </summary>
public class DatabasesConfig
{
    /// <summary>
    /// 数据库连接列表
    /// </summary>
    public required List<DatabaseConnection> Databases { get; set; }
}
