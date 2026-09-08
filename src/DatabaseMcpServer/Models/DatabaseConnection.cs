using System.Text.Json.Serialization;

namespace DatabaseMcpServer.Models;

/// <summary>
/// 数据库连接配置模型
/// </summary>
public class DatabaseConnection
{
    private bool? _enableDangerousOperations;

    /// <summary>
    /// 数据库连接名称（唯一标识）
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 数据库连接字符串
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// 数据库类型
    /// </summary>
    public string DbType { get; set; } = string.Empty;

    /// <summary>
    /// 连接描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 是否为默认连接
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// 是否允许通用命令工具执行危险操作（如 CREATE/DROP/TRUNCATE/ALTER TABLE）。默认 false。
    /// </summary>
    public bool EnableDangerousOperations
    {
        get => _enableDangerousOperations ?? false;
        set => _enableDangerousOperations = value;
    }

    // Read legacy configs, but only write the new name. The new field wins in either JSON order.
    [JsonPropertyName("allowDangerousOperations")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyAllowDangerousOperations
    {
        get => null;
        set => _enableDangerousOperations ??= value;
    }

    /// <summary>
    /// 数据库优化配置选项
    /// </summary>
    public Dictionary<string, string>? OptimizationSettings { get; set; }
}
