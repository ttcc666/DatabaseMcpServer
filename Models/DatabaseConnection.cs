namespace DatabaseMcpServer.Models;

/// <summary>
/// 数据库连接配置模型
/// </summary>
public class DatabaseConnection
{
    /// <summary>
    /// 数据库连接名称（唯一标识）
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// 数据库连接字符串
    /// </summary>
    public required string ConnectionString { get; set; }

    /// <summary>
    /// 数据库类型
    /// </summary>
    public required string DbType { get; set; }

    /// <summary>
    /// 连接描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 是否为默认连接
    /// </summary>
    public bool IsDefault { get; set; }
}
