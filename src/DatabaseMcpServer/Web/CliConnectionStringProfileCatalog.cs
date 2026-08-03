using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using DatabaseMcpServer.Cli;

namespace DatabaseMcpServer.Web;

internal sealed record CliConnectionStringFieldDefinition(
    string Key,
    string Label,
    string InputType,
    bool Required,
    bool Sensitive,
    bool Advanced,
    string? DefaultValue);

internal sealed record CliConnectionStringProfile(
    string DbType,
    string Format,
    bool SupportsWizard,
    IReadOnlyList<CliConnectionStringFieldDefinition> Fields);

internal static class CliConnectionStringProfileCatalog
{
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "password",
        "pwd"
    };

    private static readonly HashSet<string> RequiredKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "server",
        "host",
        "data source",
        "datasource"
    };

    private static readonly HashSet<string> PrimaryKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "server",
        "host",
        "port",
        "database",
        "data source",
        "datasource",
        "user",
        "uid",
        "username",
        "user id",
        "password",
        "pwd"
    };

    private static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["server"] = "服务器",
        ["host"] = "主机",
        ["port"] = "端口",
        ["database"] = "数据库",
        ["data source"] = "数据源",
        ["datasource"] = "数据源",
        ["user"] = "用户名",
        ["uid"] = "用户名",
        ["username"] = "用户名",
        ["user id"] = "用户名",
        ["password"] = "密码",
        ["pwd"] = "密码"
    };

    public static CliConnectionStringProfile Get(string dbType)
    {
        if (string.Equals(dbType, "MongoDb", StringComparison.OrdinalIgnoreCase))
        {
            return CreateMongoDbProfile();
        }

        if (!CliConfigPresetCatalog.TryGet(dbType, out var preset))
        {
            return new CliConnectionStringProfile(dbType, "raw", false, []);
        }

        try
        {
            var builder = new DbConnectionStringBuilder
            {
                ConnectionString = preset.ExampleConnectionString
            };

            var fields = builder.Keys
                .Cast<string>()
                .Select(key => CreateField(key, Convert.ToString(builder[key], CultureInfo.InvariantCulture)))
                .ToList();

            foreach (var key in GetDeclaredSensitiveKeys(preset.ExampleConnectionString))
            {
                if (fields.All(field => !string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase)))
                {
                    fields.Add(CreateField(key, null));
                }
            }

            return new CliConnectionStringProfile(preset.DbType, "keyValue", fields.Count > 0, fields);
        }
        catch (ArgumentException)
        {
            return new CliConnectionStringProfile(preset.DbType, "raw", false, []);
        }
    }

    public static bool IsSensitive(string key)
    {
        return SensitiveKeys.Contains(key);
    }

    private static IEnumerable<string> GetDeclaredSensitiveKeys(string connectionString)
    {
        return Regex.Matches(
                connectionString,
                @"(?:^|;)\s*(?<key>Password|Pwd)\s*=",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Select(match => match.Groups["key"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static CliConnectionStringFieldDefinition CreateField(string key, string? value)
    {
        var sensitive = SensitiveKeys.Contains(key);
        var inputType = sensitive
            ? "password"
            : bool.TryParse(value, out _)
                ? "boolean"
                : int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                    ? "number"
                    : "text";

        return new CliConnectionStringFieldDefinition(
            key,
            Labels.GetValueOrDefault(key, key),
            inputType,
            RequiredKeys.Contains(key),
            sensitive,
            !PrimaryKeys.Contains(key),
            sensitive ? null : value);
    }

    private static CliConnectionStringProfile CreateMongoDbProfile()
    {
        return new CliConnectionStringProfile(
            "MongoDb",
            "uri",
            true,
            [
                new("Host", "主机", "text", true, false, false, "localhost"),
                new("Port", "端口", "number", false, false, false, "27017"),
                new("Database", "数据库", "text", false, false, false, "mydb"),
                new("Username", "用户名", "text", false, false, false, "root"),
                new("Password", "密码", "password", false, true, false, null),
                new("AuthSource", "认证数据库", "text", false, false, true, "admin")
            ]);
    }
}
