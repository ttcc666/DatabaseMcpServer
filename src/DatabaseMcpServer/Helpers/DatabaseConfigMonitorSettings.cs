namespace DatabaseMcpServer.Helpers;

internal static class DatabaseConfigMonitorSettings
{
    public const string EnvironmentVariableName = "ENABLE_MONITOR_CONFIG";

    public static bool? GetEnvironmentOverride()
    {
        var value = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (IsTrue(value))
        {
            return true;
        }

        if (IsFalse(value))
        {
            return false;
        }

        return null;
    }

    public static bool IsEnabled(bool enableMonitorConfig, bool? processOverride = null)
    {
        if (processOverride.HasValue)
        {
            return processOverride.Value;
        }

        var environmentOverride = GetEnvironmentOverride();
        return environmentOverride ?? enableMonitorConfig;
    }

    private static bool IsTrue(string value)
        => value.Equals("true", StringComparison.OrdinalIgnoreCase)
           || value.Equals("1", StringComparison.OrdinalIgnoreCase)
           || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
           || value.Equals("on", StringComparison.OrdinalIgnoreCase);

    private static bool IsFalse(string value)
        => value.Equals("false", StringComparison.OrdinalIgnoreCase)
           || value.Equals("0", StringComparison.OrdinalIgnoreCase)
           || value.Equals("no", StringComparison.OrdinalIgnoreCase)
           || value.Equals("off", StringComparison.OrdinalIgnoreCase);
}
