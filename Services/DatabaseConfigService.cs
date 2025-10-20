
using SqlSugar;
using DatabaseMcpServer.Helpers;

namespace DatabaseMcpServer.Services;

/// <summary>
/// 数据库配置服务 - 从环境变量读取数据库配置。
/// </summary>
internal static class DatabaseConfigService
{
    /// <summary>
    /// 从环境变量读取数据库连接字符串。
    /// </summary>
    /// <returns>连接字符串,如果未配置则抛出异常</returns>
    /// <exception cref="InvalidOperationException">当环境变量未配置时抛出</exception>
    public static string GetConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "未配置数据库连接字符串。请在 MCP 配置文件中设置环境变量 DB_CONNECTION_STRING");
        }
        return connectionString;
    }

    /// <summary>
    /// 从环境变量读取数据库类型。
    /// </summary>
    /// <returns>数据库类型字符串,如果未配置则返回 MySql(默认值)</returns>
    public static string GetDatabaseType()
    {
        var dbType = Environment.GetEnvironmentVariable("DB_TYPE");
        if (string.IsNullOrWhiteSpace(dbType))
        {
            // 默认使用 MySql
            return "MySql";
        }
        return dbType;
    }

    /// <summary>
    /// 从环境变量读取 DbType 枚举。
    /// </summary>
    /// <returns>对应的 DbType 枚举值</returns>
    public static DbType GetParsedDbType()
    {
        var dbType = GetDatabaseType();
        return DatabaseHelper.ParseDbType(dbType);
    }

    /// <summary>
    /// 创建全局数据库客户端。
    /// </summary>
    /// <returns>配置好的 SqlSugarClient 实例</returns>
    public static SqlSugarClient CreateGlobalClient()
    {
        var connectionString = GetConnectionString();
        var dbType = GetParsedDbType();
        return DatabaseHelper.CreateClient(connectionString, dbType);
    }

    /// <summary>
    /// 验证数据库配置是否正确。
    /// </summary>
    /// <returns>如果配置正确返回 true,否则返回 false</returns>
    public static bool ValidateConfiguration()
    {
        try
        {
            GetConnectionString();
            GetParsedDbType();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 获取配置信息摘要。
    /// </summary>
    /// <returns>配置信息 JSON 字符串</returns>
    public static string GetConfigurationSummary()
    {
        var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING") ?? "未配置";
        // 隐藏连接字符串的敏感信息
        var maskedConnectionString = MaskSensitiveInfo(connectionString);

        var dbType = GetDatabaseType();
        var isValid = ValidateConfiguration();

        return DatabaseHelper.SerializeResult(new
        {
            configured = isValid,
            databaseType = dbType,
            connectionString = maskedConnectionString,
            message = isValid ? "配置有效" : "配置无效,请检查 MCP 配置文件中的环境变量"
        });
    }

    /// <summary>
    /// 隐藏连接字符串中的敏感信息。
    /// </summary>
    /// <param name="connectionString">原始连接字符串</param>
    /// <returns>隐藏敏感信息后的连接字符串</returns>
    private static string MaskSensitiveInfo(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return string.Empty;

        // 隐藏密码
        var result = System.Text.RegularExpressions.Regex.Replace(
            connectionString,
            @"(Password|pwd)=([^;])",
            "Password=****",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return result;
    }
}
