using DatabaseMcpServer.Models;

namespace DatabaseMcpServer.Configuration;

internal static class ConfigurationCommandSupport
{
    public static ConfigurationCommandResult Execute(Func<ConfigurationCommandResult> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex)
        {
            return new ConfigurationCommandResult
            {
                Success = false,
                Message = ex.Message
            };
        }
    }

    public static DatabaseConnection FindConnection(DatabasesConfig config, string name)
    {
        var connection = config.Databases.FirstOrDefault(db => string.Equals(db.Name, name, StringComparison.Ordinal));
        if (connection == null)
        {
            throw new InvalidOperationException($"数据库连接 '{name}' 不存在。");
        }

        return connection;
    }

    public static void ClearDefaultFlags(DatabasesConfig config)
    {
        foreach (var item in config.Databases)
        {
            item.IsDefault = false;
        }
    }

    public static void ValidateRequiredText(string value, string optionName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"选项 '--{optionName}' 不能为空。");
        }
    }
}
