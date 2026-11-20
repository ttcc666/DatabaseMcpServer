using System.Text.RegularExpressions;

namespace DatabaseMcpServer.Helpers;

internal static partial class ConnectionStringMasker
{
    [GeneratedRegex("(?i)(password|pwd)=([^;]*)", RegexOptions.Compiled)]
    private static partial Regex SensitiveInfoPattern();

    public static string Mask(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return string.Empty;
        }

        return SensitiveInfoPattern().Replace(connectionString, match =>
        {
            var key = match.Groups[1].Value;
            return $"{key}=****";
        });
    }
}
