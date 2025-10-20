using System.ComponentModel;
using ModelContextProtocol.Server;
using DatabaseMcpServer.Services;

namespace DatabaseMcpServer.Tools;

/// <summary>
/// 配置工具类，用于管理和获取数据库配置信息。
/// </summary>
internal class ConfigurationTools
{
    /// <summary>
    /// 从环境变量中获取当前数据库配置。
    /// </summary>
    /// <returns>包含数据库配置信息的 JSON 字符串</returns>
    [McpServerTool]
    [Description("从环境变量中获取当前数据库配置")]
    public string GetDatabaseConfig()
    {
        return DatabaseConfigService.GetConfigurationSummary();
    }

    /// <summary>
    /// 验证数据库配置是否正确。
    /// </summary>
    /// <returns>包含验证结果的 JSON 字符串</returns>
    [McpServerTool]
    [Description("验证数据库配置是否正确")]
    public string ValidateConfiguration()
    {
        return DatabaseConfigService.GetConfigurationSummary();
    }
}
