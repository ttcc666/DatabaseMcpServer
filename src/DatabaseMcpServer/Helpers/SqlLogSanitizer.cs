using SqlSugar;
using System.Globalization;

namespace DatabaseMcpServer.Helpers;

internal static class SqlLogSanitizer
{
    private static readonly string[] SensitiveKeywords =
    [
        "password",
        "pwd",
        "secret",
        "token",
        "apikey",
        "api_key",
        "accesskey",
        "access_key",
        "privatekey",
        "private_key",
        "credential",
        "cert",
        "signature"
    ];

    public static string FormatParametersForLog(SugarParameter[]? parameters)
    {
        if (parameters == null || parameters.Length == 0)
        {
            return "无参数";
        }

        return string.Join(", ", parameters.Select(FormatSingleParameter));
    }

    public static string SanitizeSqlForLog(string? sql, int maxLength = 500)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return string.Empty;
        }

        var normalized = sql.Replace('\r', ' ').Replace('\n', ' ');
        return normalized.Length > maxLength ? $"{normalized[..maxLength]}..." : normalized;
    }

    private static string FormatSingleParameter(SugarParameter parameter)
    {
        var parameterName = parameter.ParameterName ?? string.Empty;
        var parameterValue = IsSensitiveParameterName(parameterName)
            ? "****"
            : FormatValue(parameter.Value);

        return $"{parameterName}={parameterValue}";
    }

    private static string FormatValue(object? value)
    {
        if (value == null || value == DBNull.Value)
        {
            return "null";
        }

        return value switch
        {
            byte[] bytes => $"[binary:{bytes.Length}]",
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
            _ => Truncate(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)
        };
    }

    private static bool IsSensitiveParameterName(string parameterName)
    {
        var normalized = parameterName.TrimStart('@').Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        return SensitiveKeywords.Any(keyword => normalized.Contains(keyword.Replace("_", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal));
    }

    private static string Truncate(string value, int maxLength = 120)
    {
        return value.Length > maxLength
            ? $"{value[..maxLength]}...(len={value.Length})"
            : value;
    }
}
