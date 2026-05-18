using System.Text;

namespace DatabaseMcpServer.Cli;

internal sealed record CliCommandOptionMetadata(
    string OptionName,
    string Description,
    Type OptionType,
    bool IsRequired,
    object? DefaultValue = null)
{
    public bool IsBoolean => OptionType == typeof(bool);

    public string DisplayTypeName => OptionType == typeof(string)
        ? "string"
        : OptionType == typeof(bool)
            ? "bool"
            : OptionType.Name;
}

internal sealed record CliCommandMetadata(
    string Name,
    string Description,
    string Usage,
    IReadOnlyList<CliCommandOptionMetadata> Options,
    bool RequiresConfirmation = false);

internal static class CliBuiltinCommandCatalog
{
    public static CliCommandMetadata Web { get; } = new(
        "-web",
        "启动本地 Web 配置管理页，并默认自动打开浏览器。",
        "DatabaseMcpServer -web [--config path] [--port 0-65535] [--no-browser true|false]",
        [
            new CliCommandOptionMetadata("port", "本地 Web 服务端口；不传时自动分配可用端口。", typeof(int), false, null),
            new CliCommandOptionMetadata("no-browser", "启动后不自动打开浏览器。", typeof(bool), false, false)
        ]);

    public static CliCommandMetadata Init { get; } = new(
        "init",
        "初始化本地 databases.json 配置文件；默认写入 %USERPROFILE%/.database-mcp/databases.json。",
        "DatabaseMcpServer init [--config path] [--force]",
        [
            new CliCommandOptionMetadata("force", "覆盖已存在的配置文件。", typeof(bool), false, false)
        ]);

    public static IReadOnlyDictionary<string, CliCommandMetadata> ConfigCommands { get; } =
        new Dictionary<string, CliCommandMetadata>(StringComparer.Ordinal)
        {
            ["list"] = new(
                "config list",
                "列出配置文件中的所有数据库连接。",
                "DatabaseMcpServer config list [--config path]",
                []),
            ["show"] = new(
                "config show",
                "显示指定连接的详情（连接字符串会脱敏）。",
                "DatabaseMcpServer config show --name <name> [--config path]",
                [
                    new CliCommandOptionMetadata("name", "数据库连接名称。", typeof(string), true)
                ]),
            ["presets"] = new(
                "config presets",
                "列出内置数据库连接模板。",
                "DatabaseMcpServer config presets",
                []),
            ["preset"] = new(
                "config preset",
                "查看指定数据库类型的内置连接模板。",
                "DatabaseMcpServer config preset --db-type <type>",
                [
                    new CliCommandOptionMetadata("db-type", "数据库类型，例如 SqlServer / Sqlite / PostgreSQL。", typeof(string), true)
                ]),
            ["create"] = new(
                "config create",
                "基于内置模板创建一个新的数据库连接。",
                "DatabaseMcpServer config create --from-preset <type> [--name <name>] [--connection-string <value>] [--description <text>] [--set-default true|false] [--print-only true|false] [--config path]",
                [
                    new CliCommandOptionMetadata("from-preset", "内置模板数据库类型，例如 Sqlite / SqlServer。", typeof(string), true),
                    new CliCommandOptionMetadata("name", "新数据库连接名称；默认使用模板示例名称。", typeof(string), false, null),
                    new CliCommandOptionMetadata("connection-string", "覆盖模板中的示例连接字符串。", typeof(string), false, null),
                    new CliCommandOptionMetadata("description", "覆盖模板中的描述。", typeof(string), false, null),
                    new CliCommandOptionMetadata("set-default", "是否将新连接设为默认连接。", typeof(bool), false, false),
                    new CliCommandOptionMetadata("print-only", "仅输出将创建的连接，不写入配置文件。", typeof(bool), false, false)
                ]),
            ["add"] = new(
                "config add",
                "向配置文件新增一个数据库连接。",
                "DatabaseMcpServer config add --name <name> --db-type <type> --connection-string <value> [--description <text>] [--set-default true|false] [--config path]",
                [
                    new CliCommandOptionMetadata("name", "数据库连接名称。", typeof(string), true),
                    new CliCommandOptionMetadata("db-type", "数据库类型，例如 MySql / SqlServer / Sqlite。", typeof(string), true),
                    new CliCommandOptionMetadata("connection-string", "数据库连接字符串。", typeof(string), true),
                    new CliCommandOptionMetadata("description", "连接描述。", typeof(string), false, null),
                    new CliCommandOptionMetadata("set-default", "是否设为默认连接。", typeof(bool), false, false)
                ]),
            ["rename"] = new(
                "config rename",
                "重命名一个已有数据库连接。",
                "DatabaseMcpServer config rename --name <name> --new-name <new-name> [--config path]",
                [
                    new CliCommandOptionMetadata("name", "当前数据库连接名称。", typeof(string), true),
                    new CliCommandOptionMetadata("new-name", "新的数据库连接名称。", typeof(string), true)
                ]),
            ["update"] = new(
                "config update",
                "更新已有数据库连接的部分字段。",
                "DatabaseMcpServer config update --name <name> [--db-type <type>] [--connection-string <value>] [--description <text>] [--clear-description true|false] [--set-default true|false] [--config path]",
                [
                    new CliCommandOptionMetadata("name", "数据库连接名称。", typeof(string), true),
                    new CliCommandOptionMetadata("db-type", "新的数据库类型。", typeof(string), false, null),
                    new CliCommandOptionMetadata("connection-string", "新的连接字符串。", typeof(string), false, null),
                    new CliCommandOptionMetadata("description", "新的连接描述。", typeof(string), false, null),
                    new CliCommandOptionMetadata("clear-description", "显式清空 description。", typeof(bool), false, false),
                    new CliCommandOptionMetadata("set-default", "是否设为默认连接。", typeof(bool), false, false)
                ]),
            ["clone"] = new(
                "config clone",
                "复制一个已有数据库连接为新连接。",
                "DatabaseMcpServer config clone --name <name> --new-name <new-name> [--set-default true|false] [--config path]",
                [
                    new CliCommandOptionMetadata("name", "源数据库连接名称。", typeof(string), true),
                    new CliCommandOptionMetadata("new-name", "新数据库连接名称。", typeof(string), true),
                    new CliCommandOptionMetadata("set-default", "是否将克隆结果设为默认连接。", typeof(bool), false, false)
                ]),
            ["remove"] = new(
                "config remove",
                "从配置文件删除指定连接。",
                "DatabaseMcpServer config remove --name <name> [--config path] [--yes]",
                [
                    new CliCommandOptionMetadata("name", "数据库连接名称。", typeof(string), true)
                ],
                RequiresConfirmation: true),
            ["set-default"] = new(
                "config set-default",
                "将指定连接设为唯一默认连接。",
                "DatabaseMcpServer config set-default --name <name> [--config path]",
                [
                    new CliCommandOptionMetadata("name", "数据库连接名称。", typeof(string), true)
                ]),
            ["use"] = new(
                "config use",
                "将指定连接设为默认连接，作为 set-default 的易用别名。",
                "DatabaseMcpServer config use --name <name> [--config path]",
                [
                    new CliCommandOptionMetadata("name", "数据库连接名称。", typeof(string), true)
                ]),
            ["test"] = new(
                "config test",
                "测试指定连接是否可达。",
                "DatabaseMcpServer config test --name <name> [--config path]",
                [
                    new CliCommandOptionMetadata("name", "数据库连接名称。", typeof(string), true)
                ]),
            ["validate"] = new(
                "config validate",
                "校验配置文件内容是否合法，不执行数据库连通性测试。",
                "DatabaseMcpServer config validate [--config path]",
                []),
            ["doctor"] = new(
                "config doctor",
                "诊断配置文件，并可选测试每个连接的连通性。",
                "DatabaseMcpServer config doctor [--config path] [--name <name>] [--test-connections true|false] [--fix-suggestions true|false] [--summary-only true|false]",
                [
                    new CliCommandOptionMetadata("name", "仅诊断指定数据库连接。", typeof(string), false, null),
                    new CliCommandOptionMetadata("test-connections", "是否逐个测试连接，默认 true。", typeof(bool), false, true),
                    new CliCommandOptionMetadata("fix-suggestions", "是否输出修复建议，默认 true。", typeof(bool), false, true),
                    new CliCommandOptionMetadata("summary-only", "仅输出摘要字段，适合脚本读取。", typeof(bool), false, false)
                ]),
            ["export"] = new(
                "config export",
                "将当前配置文件导出到指定路径。",
                "DatabaseMcpServer config export --output <path> [--config path] [--force]",
                [
                    new CliCommandOptionMetadata("output", "导出文件路径。", typeof(string), true),
                    new CliCommandOptionMetadata("force", "覆盖已存在的导出文件。", typeof(bool), false, false)
                ]),
            ["import"] = new(
                "config import",
                "从指定路径导入配置文件到当前目标路径。",
                "DatabaseMcpServer config import --input <path> [--config path] [--force]",
                [
                    new CliCommandOptionMetadata("input", "导入文件路径。", typeof(string), true),
                    new CliCommandOptionMetadata("force", "目标文件已存在时允许覆盖。", typeof(bool), false, false)
                ])
        };

    public static bool TryGetConfigCommand(string name, out CliCommandMetadata metadata)
    {
        return ConfigCommands.TryGetValue(name, out metadata!);
    }

    public static string WriteConfigRootHelp()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Usage:");
        builder.AppendLine("  DatabaseMcpServer config list [--config path]");
        builder.AppendLine("  DatabaseMcpServer config show --name <name> [--config path]");
        builder.AppendLine("  DatabaseMcpServer config presets");
        builder.AppendLine("  DatabaseMcpServer config preset --db-type <type>");
        builder.AppendLine("  DatabaseMcpServer config create --from-preset <type> [--name <name>] [--connection-string <value>] [--description <text>] [--set-default true|false] [--print-only true|false] [--config path]");
        builder.AppendLine("  DatabaseMcpServer config add --name <name> --db-type <type> --connection-string <value> [--description <text>] [--set-default true|false] [--config path]");
        builder.AppendLine("  DatabaseMcpServer config rename --name <name> --new-name <new-name> [--config path]");
        builder.AppendLine("  DatabaseMcpServer config update --name <name> [--db-type <type>] [--connection-string <value>] [--description <text>] [--clear-description true|false] [--set-default true|false] [--config path]");
        builder.AppendLine("  DatabaseMcpServer config clone --name <name> --new-name <new-name> [--set-default true|false] [--config path]");
        builder.AppendLine("  DatabaseMcpServer config remove --name <name> [--config path] [--yes]");
        builder.AppendLine("  DatabaseMcpServer config set-default --name <name> [--config path]");
        builder.AppendLine("  DatabaseMcpServer config use --name <name> [--config path]");
        builder.AppendLine("  DatabaseMcpServer config test --name <name> [--config path]");
        builder.AppendLine("  DatabaseMcpServer config validate [--config path]");
        builder.AppendLine("  DatabaseMcpServer config doctor [--config path] [--name <name>] [--test-connections true|false] [--fix-suggestions true|false] [--summary-only true|false]");
        builder.AppendLine("  DatabaseMcpServer config export --output <path> [--config path] [--force]");
        builder.AppendLine("  DatabaseMcpServer config import --input <path> [--config path] [--force]");
        builder.AppendLine();
        builder.AppendLine("Notes:");
        builder.AppendLine("  Config commands default to %USERPROFILE%/.database-mcp/databases.json.");
        builder.AppendLine("  Use --config to override the target file for this invocation.");
        builder.AppendLine("  Config command results are written to stdout as JSON.");
        builder.AppendLine();
        builder.AppendLine("Examples:");
        builder.AppendLine("  DatabaseMcpServer init");
        builder.AppendLine("  DatabaseMcpServer config list");
        builder.AppendLine("  DatabaseMcpServer config presets");
        builder.AppendLine("  DatabaseMcpServer config create --from-preset 'Sqlite' --name 'sqlite-local' --connection-string 'Data Source=./data/local.db;Cache=Shared;Mode=ReadWriteCreate;'");
        builder.AppendLine("  DatabaseMcpServer config add --name 'sqlite-local' --db-type 'Sqlite' --connection-string 'Data Source=./data/local.db;Cache=Shared;Mode=ReadWriteCreate;' --set-default");
        builder.AppendLine("  DatabaseMcpServer config rename --name 'sqlite-local' --new-name 'sqlite-dev'");
        builder.AppendLine("  DatabaseMcpServer config clone --name 'sqlite-dev' --new-name 'sqlite-ci'");
        builder.AppendLine("  DatabaseMcpServer config doctor");
        builder.AppendLine("  DatabaseMcpServer config export --output '.\\backup-databases.json'");
        return builder.ToString();
    }
}
