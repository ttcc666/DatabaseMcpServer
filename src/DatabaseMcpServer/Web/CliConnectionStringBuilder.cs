using System.Data.Common;
using System.Globalization;

namespace DatabaseMcpServer.Web;

internal sealed class CliConnectionStringBuilder
{
    public string Build(string dbType, IReadOnlyDictionary<string, string?> values)
    {
        var profile = CliConnectionStringProfileCatalog.Get(dbType);
        if (!profile.SupportsWizard)
        {
            throw new InvalidOperationException($"数据库类型 '{dbType}' 不支持连接字符串向导，请使用原始模式。");
        }

        var definitions = profile.Fields.ToDictionary(field => field.Key, StringComparer.OrdinalIgnoreCase);
        var unknownField = values.Keys.FirstOrDefault(key => !definitions.ContainsKey(key));
        if (unknownField != null)
        {
            throw new InvalidOperationException($"连接字段 '{unknownField}' 不受数据库类型 '{dbType}' 支持。");
        }

        foreach (var field in profile.Fields.Where(field => field.Required))
        {
            if (!TryGetValue(values, field.Key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"连接字段 '{field.Label}' 不能为空。");
            }
        }

        ValidateTypedValues(profile, values);

        return string.Equals(profile.Format, "uri", StringComparison.Ordinal)
            ? BuildMongoDbUri(values)
            : BuildKeyValue(profile, values);
    }

    private static string BuildKeyValue(
        CliConnectionStringProfile profile,
        IReadOnlyDictionary<string, string?> values)
    {
        var builder = new DbConnectionStringBuilder();
        foreach (var field in profile.Fields)
        {
            if (TryGetValue(values, field.Key, out var value))
            {
                builder[field.Key] = value ?? string.Empty;
            }
        }

        if (builder.Count == 0)
        {
            throw new InvalidOperationException("至少填写一个连接字段。");
        }

        return builder.ConnectionString;
    }

    private static string BuildMongoDbUri(IReadOnlyDictionary<string, string?> values)
    {
        var host = GetValue(values, "Host")!;
        var port = ParseOptionalPort(GetValue(values, "Port"));
        var database = GetValue(values, "Database");
        var username = GetValue(values, "Username");
        var password = GetValue(values, "Password");
        var authSource = GetValue(values, "AuthSource");

        if (string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("填写密码时必须同时填写用户名。");
        }

        var builder = new UriBuilder("mongodb", host, port ?? 27017)
        {
            Path = string.IsNullOrWhiteSpace(database) ? string.Empty : database,
            UserName = username ?? string.Empty,
            Password = string.IsNullOrEmpty(username) ? string.Empty : password ?? string.Empty,
            Query = string.IsNullOrWhiteSpace(authSource)
                ? string.Empty
                : $"authSource={Uri.EscapeDataString(authSource)}"
        };

        return builder.Uri.AbsoluteUri;
    }

    private static void ValidateTypedValues(
        CliConnectionStringProfile profile,
        IReadOnlyDictionary<string, string?> values)
    {
        foreach (var field in profile.Fields)
        {
            if (!TryGetValue(values, field.Key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (string.Equals(field.InputType, "number", StringComparison.Ordinal) &&
                !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                throw new InvalidOperationException($"连接字段 '{field.Label}' 需要整数值。");
            }

            if (string.Equals(field.InputType, "boolean", StringComparison.Ordinal) && !bool.TryParse(value, out _))
            {
                throw new InvalidOperationException($"连接字段 '{field.Label}' 需要 true 或 false。");
            }
        }
    }

    private static int? ParseOptionalPort(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
        {
            throw new InvalidOperationException("连接字段 '端口' 必须在 1-65535 之间。");
        }

        return port;
    }

    private static string? GetValue(IReadOnlyDictionary<string, string?> values, string key)
    {
        return TryGetValue(values, key, out var value) ? value : null;
    }

    private static bool TryGetValue(
        IReadOnlyDictionary<string, string?> values,
        string key,
        out string? value)
    {
        var match = values.FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase));
        value = match.Value;
        return match.Key != null;
    }
}
