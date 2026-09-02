using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DatabaseMcpServer.Gui.Core.Services;

/// <summary>
/// 按数据库类型提供连接字符串的必填/可选字段，并负责解析和组装连接字符串。
/// 字段定义与 DatabaseSetting 中的配置说明保持一致。
/// </summary>
public static class DbTypeConnectionFields
{
    public sealed class Field
    {
        public Field(string key, string label, bool isPassword = false, string? defaultValue = null)
        {
            Key = key;
            Label = label;
            IsPassword = isPassword;
            DefaultValue = defaultValue;
        }

        public string Key { get; }
        public string Label { get; }
        public bool IsPassword { get; }
        public string? DefaultValue { get; }
    }

    private static Field F(string key, string label, bool password = false, string? defaultValue = null) =>
        new(key, label, password, defaultValue);

    private static readonly IReadOnlyList<Field> Mysql =
    [F("Server", "主机", defaultValue: "localhost"), F("Port", "端口", defaultValue: "3306"), F("Database", "数据库"), F("User", "用户名", defaultValue: "root"), F("Password", "密码", true)];
    private static readonly IReadOnlyList<Field> Postgres =
    [F("Host", "主机", defaultValue: "localhost"), F("Port", "端口", defaultValue: "5432"), F("Database", "数据库"), F("Username", "用户名", defaultValue: "postgres"), F("Password", "密码", true)];
    private static readonly IReadOnlyList<Field> SqlServer =
    [F("Server", "主机", defaultValue: "localhost"), F("Database", "数据库"), F("User Id", "用户名"), F("Password", "密码", true), F("Encrypt", "启用加密", defaultValue: "True"), F("TrustServerCertificate", "信任服务器证书", defaultValue: "True")];
    private static readonly IReadOnlyList<Field> Oracle =
    [F("Data Source", "数据源"), F("User ID", "用户名"), F("Password", "密码", true)];
    private static readonly IReadOnlyList<Field> Mongo =
    [F("Host", "主机", defaultValue: "localhost"), F("Port", "端口", defaultValue: "27017"), F("Database", "数据库"), F("Username", "用户名"), F("Password", "密码", true)];
    private static readonly IReadOnlyList<Field> Sqlite = [F("Data Source", "数据库文件路径")];
    private static readonly IReadOnlyList<Field> Duckdb = [F("DataSource", "数据库文件路径")];
    private static readonly IReadOnlyList<Field> ClickHouse =
    [F("Host", "主机", defaultValue: "localhost"), F("Port", "端口", defaultValue: "8123"), F("Database", "数据库"), F("User", "用户名", defaultValue: "default"), F("Password", "密码", true)];
    private static readonly IReadOnlyList<Field> Doris =
    [F("Host", "主机", defaultValue: "localhost"), F("Port", "端口", defaultValue: "9030"), F("Database", "数据库"), F("User", "用户名", defaultValue: "root"), F("Password", "密码", true)];
    private static readonly IReadOnlyList<Field> Dm =
    [F("Server", "服务器", defaultValue: "localhost:5236"), F("User Id", "用户名", defaultValue: "SYSDBA"), F("PWD", "密码", true), F("DATABASE", "数据库")];
    private static readonly IReadOnlyList<Field> Gbase =
    [F("Host", "主机", defaultValue: "localhost"), F("Service", "服务端口", defaultValue: "19088"), F("Server", "服务器"), F("Database", "数据库"), F("Uid", "用户名"), F("Pwd", "密码", true)];
    private static readonly IReadOnlyList<Field> GoldenDb =
    [F("Server", "主机", defaultValue: "localhost"), F("Port", "端口", defaultValue: "3306"), F("Database", "数据库"), F("Uid", "用户名", defaultValue: "root"), F("Pwd", "密码", true)];
    private static readonly IReadOnlyList<Field> HighGo =
    [F("Server", "主机", defaultValue: "localhost"), F("Port", "端口", defaultValue: "5866"), F("UId", "用户名"), F("Password", "密码", true), F("Database", "数据库")];
    private static readonly IReadOnlyList<Field> Kdbndp =
    [F("Server", "主机", defaultValue: "localhost"), F("Port", "端口", defaultValue: "54321"), F("UID", "用户名"), F("PWD", "密码", true), F("database", "数据库")];
    private static readonly IReadOnlyList<Field> Oscar =
    [F("Server", "服务器", defaultValue: "localhost:2003"), F("User Id", "用户名"), F("Password", "密码", true), F("Database", "数据库")];
    private static readonly IReadOnlyList<Field> Vastbase =
    [F("HOST", "主机", defaultValue: "localhost"), F("PORT", "端口", defaultValue: "5432"), F("DATABASE", "数据库"), F("USER ID", "用户名"), F("PASSWORD", "密码", true)];
    private static readonly IReadOnlyList<Field> QuestDb =
    [F("host", "主机", defaultValue: "localhost"), F("port", "端口", defaultValue: "8812"), F("username", "用户名", defaultValue: "admin"), F("password", "密码", true), F("database", "数据库")];
    private static readonly IReadOnlyList<Field> Tdengine =
    [F("Host", "主机", defaultValue: "localhost"), F("Port", "端口", defaultValue: "6030"), F("Username", "用户名", defaultValue: "root"), F("Password", "密码", true), F("Database", "数据库")];

    private static readonly IReadOnlyList<Field> Empty = Array.Empty<Field>();

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<Field>> Required =
        new Dictionary<string, IReadOnlyList<Field>>(StringComparer.OrdinalIgnoreCase)
        {
            ["mysql"] = Mysql, ["polardb"] = Mysql, ["tidb"] = Mysql, ["oceanbase"] = Mysql,
            ["oceanbasefororacle"] = Oracle,
            ["postgresql"] = Postgres, ["opengauss"] = Postgres, ["gaussdb"] = Postgres, ["gaussdbnative"] = Postgres,
            ["sqlserver"] = SqlServer, ["oracle"] = Oracle, ["mongodb"] = Mongo, ["sqlite"] = Sqlite, ["duckdb"] = Duckdb,
            ["clickhouse"] = ClickHouse, ["doris"] = Doris, ["dm"] = Dm, ["gbase"] = Gbase, ["goldendb"] = GoldenDb,
            ["hg"] = HighGo, ["kdbndp"] = Kdbndp, ["kingbase"] = Kdbndp, ["oscar"] = Oscar, ["vastbase"] = Vastbase,
            ["questdb"] = QuestDb, ["tdengine"] = Tdengine
        };

    // 连接串「性能优化参数（推荐）」与「可选参数」分列，来源 DatabaseSetting/<Type>.md。
    // 两节重复的键只保留在性能优化参数中。
    private static IReadOnlyList<Field> MysqlPerformance =>
    [
        F("Charset", "字符集", defaultValue: "utf8mb4"),
        F("AllowLoadLocalInfile", "启用批量导入", defaultValue: "true"),
        F("Pooling", "启用连接池", defaultValue: "true"),
        F("Min Pool Size", "最小连接数", defaultValue: "1"),
        F("Max Pool Size", "最大连接数", defaultValue: "100"),
        F("Allow User Variables", "支持用户变量", defaultValue: "True"),
        F("Connection Timeout", "连接超时", defaultValue: "30")
    ];

    private static IReadOnlyList<Field> MysqlOptional =>
    [
        F("SslMode", "SSL 模式", defaultValue: "None"),
        F("AllowPublicKeyRetrieval", "允许公钥检索", defaultValue: "false"),
        F("Convert Zero Datetime", "转换零日期", defaultValue: "false"),
        F("Allow Zero Datetime", "允许零日期", defaultValue: "false"),
        F("TreatTinyAsBoolean", "tinyint(1) 作为布尔", defaultValue: "true")
    ];

    private static IReadOnlyList<Field> PostgresPerformance =>
    [
        F("Pooling", "启用连接池", defaultValue: "true"),
        F("Minimum Pool Size", "最小连接数", defaultValue: "1"),
        F("Maximum Pool Size", "最大连接数", defaultValue: "100"),
        F("Timeout", "连接超时", defaultValue: "30"),
        F("Command Timeout", "命令超时", defaultValue: "30"),
        F("Search Path", "架构搜索路径", defaultValue: "public"),
        F("MaxPoolSize", "连接池大小（兼容写法）")
    ];

    private static IReadOnlyList<Field> PostgresOptional =>
    [
        F("SSL Mode", "SSL 模式", defaultValue: "Disable"),
        F("Application Name", "应用程序名称"),
        F("Keepalive", "保持连接活跃", defaultValue: "0")
    ];

    private static IReadOnlyList<Field> GaussDbPerformance =>
    [
        F("Pooling", "启用连接池", defaultValue: "true"),
        F("Minimum Pool Size", "最小连接数", defaultValue: "1"),
        F("Maximum Pool Size", "最大连接数", defaultValue: "100"),
        F("No Reset On Close", "关闭时不重置", defaultValue: "true"),
        F("Connection Timeout", "连接超时", defaultValue: "30"),
        F("Command Timeout", "命令超时", defaultValue: "30")
    ];

    private static IReadOnlyList<Field> GaussDbOptional =>
    [
        F("SSL Mode", "SSL 模式", defaultValue: "Disable"),
        F("Trust Server Certificate", "信任服务器证书", defaultValue: "false"),
        F("Encoding", "字符编码", defaultValue: "UTF8"),
        F("Search Path", "Schema 搜索路径", defaultValue: "public")
    ];

    private static IReadOnlyList<Field> KdbndpPerformance =>
    [
        F("Pooling", "启用连接池", defaultValue: "true"),
        F("Minimum Pool Size", "最小连接数", defaultValue: "1"),
        F("Maximum Pool Size", "最大连接数", defaultValue: "100"),
        F("Connection Timeout", "连接超时", defaultValue: "30"),
        F("Command Timeout", "命令超时", defaultValue: "30"),
        F("CursorAsDataRead", "游标读取", defaultValue: "true")
    ];

    private static IReadOnlyList<Field> KdbndpOptional =>
    [
        F("SSL Mode", "SSL 模式", defaultValue: "Disable"),
        F("Trust Server Certificate", "信任服务器证书", defaultValue: "false"),
        F("Encoding", "字符编码", defaultValue: "UTF8")
    ];

    private static IReadOnlyList<Field> MysqlOptimization =>
    [
        F("enableBulkCopy", "启用 BulkCopy", defaultValue: "false"),
        F("disableNvarchar", "禁用 Nvarchar", defaultValue: "false"),
        F("maxPoolSize", "最大连接池", defaultValue: "100"),
        F("charset", "字符集", defaultValue: "utf8mb4"),
        F("enableSsl", "启用 SSL", defaultValue: "false"),
        F("allowUserVariables", "允许用户变量", defaultValue: "false")
    ];

    private static IReadOnlyList<Field> OracleOptimization =>
    [
        F("camelCase", "驼峰命名", defaultValue: "false"),
        F("enableIdentity", "启用 Identity", defaultValue: "false"),
        F("maxParamLength", "参数名长度"),
        F("disableNvarchar", "禁用 Nvarchar", defaultValue: "false")
    ];

    private static IReadOnlyList<Field> GaussDbOptimization =>
    [
        F("nativeDriver", "原生驱动", defaultValue: "false"),
        F("isOpenGauss", "OpenGauss 模式", defaultValue: "false"),
        F("schema", "Schema", defaultValue: "public"),
        F("typeMapping", "类型映射", defaultValue: "false"),
        F("batchSize", "批量大小", defaultValue: "1000"),
        F("maxPoolSize", "最大连接池", defaultValue: "100")
    ];

    private static IReadOnlyList<Field> KdbndpOptimization =>
    [
        F("mode", "模式", defaultValue: "Oracle"),
        F("camelCase", "驼峰命名", defaultValue: "false"),
        F("enableCursor", "启用游标", defaultValue: "false"),
        F("enableJson", "启用 JSON", defaultValue: "false"),
        F("enableGeometry", "启用 Geometry", defaultValue: "false"),
        F("enableArray", "启用数组", defaultValue: "false"),
        F("schema", "Schema"),
        F("maxPoolSize", "最大连接池", defaultValue: "100"),
        F("disableNvarchar", "禁用 Nvarchar", defaultValue: "false")
    ];

    // optimizationSettings 的键定义。键名、说明、默认值与 DatabaseSetting 文档一致。
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<Field>> Optimization =
        new Dictionary<string, IReadOnlyList<Field>>(StringComparer.OrdinalIgnoreCase)
        {
            ["mysql"] = MysqlOptimization,
            ["polardb"] = MysqlOptimization,
            ["tidb"] =
            [
                F("enableHints", "启用 Hints", defaultValue: "false"),
                F("pessimisticTxn", "悲观事务", defaultValue: "false"),
                F("maxPoolSize", "最大连接池", defaultValue: "100"),
                F("enableBulkCopy", "启用 BulkCopy", defaultValue: "false"),
                F("disableNvarchar", "禁用 Nvarchar", defaultValue: "false")
            ],
            ["oceanbase"] =
            [
                F("disablePooling", "禁用连接池", defaultValue: "false"),
                F("enableHints", "启用 Hints", defaultValue: "false"),
                F("maxPoolSize", "最大连接池", defaultValue: "100"),
                F("tenantMode", "租户模式", defaultValue: "mysql"),
                F("enableBulkCopy", "启用 BulkCopy", defaultValue: "false"),
                F("disableNvarchar", "禁用 Nvarchar", defaultValue: "false")
            ],
            ["oceanbasefororacle"] = OracleOptimization,
            ["postgresql"] =
            [
                F("autoToLower", "表名转小写", defaultValue: "true"),
                F("enableILike", "启用 ILIKE", defaultValue: "false"),
                F("identityStrategy", "自增策略", defaultValue: "Serial"),
                F("autoToLowerCodeFirst", "CodeFirst 转小写", defaultValue: "false")
            ],
            ["opengauss"] =
            [
                F("nativeDriver", "原生驱动", defaultValue: "false"),
                F("isOpenGauss", "OpenGauss 模式", defaultValue: "true"),
                F("schema", "Schema", defaultValue: "public"),
                F("typeMapping", "类型映射", defaultValue: "false"),
                F("batchSize", "批量大小", defaultValue: "1000"),
                F("maxPoolSize", "最大连接池", defaultValue: "100")
            ],
            ["gaussdb"] = GaussDbOptimization,
            ["gaussdbnative"] =
            [
                F("nativeDriver", "原生驱动", defaultValue: "true"),
                F("isOpenGauss", "OpenGauss 模式", defaultValue: "false"),
                F("schema", "Schema", defaultValue: "public"),
                F("typeMapping", "类型映射", defaultValue: "false"),
                F("batchSize", "批量大小", defaultValue: "1000"),
                F("maxPoolSize", "最大连接池", defaultValue: "100")
            ],
            ["dm"] =
            [
                F("lowercaseTables", "表名转小写", defaultValue: "false"),
                F("dockerMysqlMode", "Docker MySQL 模式", defaultValue: "false"),
                F("schema", "Schema"),
                F("clobOptimization", "CLOB 优化", defaultValue: "false"),
                F("maxPoolSize", "最大连接池", defaultValue: "100")
            ],
            // DatabaseSetting 无 Doris.md；保留现有键以免已保存配置无法编辑。
            ["doris"] = [F("disableNvarchar", "禁用 Nvarchar"), F("disablePooling", "禁用连接池"), F("maxPoolSize", "最大连接池")],
            ["goldendb"] = [F("disablePooling", "禁用连接池", defaultValue: "true"), F("maxPoolSize", "最大连接池", defaultValue: "50")],
            ["hg"] = [F("autoToLower", "表名转小写", defaultValue: "true"), F("disablePooling", "禁用连接池", defaultValue: "true"), F("maxPoolSize", "最大连接池", defaultValue: "100")],
            ["oscar"] = [F("maxPoolSize", "最大连接池", defaultValue: "100")],
            ["kdbndp"] = KdbndpOptimization,
            ["kingbase"] = KdbndpOptimization,
            ["oracle"] = OracleOptimization,
            ["sqlserver"] =
            [
                F("disableNvarchar", "禁用 Nvarchar", defaultValue: "false"),
                F("enableNoLock", "启用 NoLock", defaultValue: "true"),
                F("disableNoLockWithTran", "事务内禁用 NoLock", defaultValue: "true")
            ],
            ["mongodb"] = [F("maxPoolSize", "最大连接池", defaultValue: "100")],
            ["sqlite"] =
            [
                F("enableDefaultValue", "启用默认值", defaultValue: "true"),
                F("enableDescription", "启用描述", defaultValue: "true"),
                F("enableDropColumn", "启用删除列", defaultValue: "false")
            ],
            ["clickhouse"] = [F("maxPoolSize", "最大连接池", defaultValue: "50")],
            ["vastbase"] =
            [
                F("autoToLower", "表名转小写", defaultValue: "true"),
                F("noResetOnClose", "关闭时不重置", defaultValue: "true"),
                F("maxPoolSize", "最大连接池", defaultValue: "100"),
                F("disableNvarchar", "禁用 Nvarchar", defaultValue: "false")
            ]
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<Field>> Performance =
        new Dictionary<string, IReadOnlyList<Field>>(StringComparer.OrdinalIgnoreCase)
        {
            ["mysql"] = MysqlPerformance,
            ["polardb"] = MysqlPerformance,
            ["tidb"] =
            [
                F("Charset", "字符集", defaultValue: "utf8mb4"),
                F("AllowLoadLocalInfile", "启用批量导入", defaultValue: "true"),
                F("Pooling", "启用连接池", defaultValue: "true"),
                F("Min Pool Size", "最小连接数", defaultValue: "5"),
                F("Max Pool Size", "最大连接数", defaultValue: "100"),
                F("Connection Timeout", "连接超时", defaultValue: "30")
            ],
            ["oceanbase"] =
            [
                F("Charset", "字符集", defaultValue: "utf8mb4"),
                F("Pooling", "启用连接池", defaultValue: "true"),
                F("Min Pool Size", "最小连接数", defaultValue: "5"),
                F("Max Pool Size", "最大连接数", defaultValue: "100"),
                F("Connection Timeout", "连接超时", defaultValue: "30")
            ],
            ["postgresql"] = PostgresPerformance,
            ["opengauss"] = GaussDbPerformance,
            ["gaussdb"] = GaussDbPerformance,
            ["gaussdbnative"] = GaussDbPerformance,
            ["sqlserver"] =
            [
                F("Min Pool Size", "最小连接数", defaultValue: "1"),
                F("Max Pool Size", "最大连接数", defaultValue: "100"),
                F("Connection Timeout", "连接超时", defaultValue: "30"),
                F("Pooling", "启用连接池", defaultValue: "true")
            ],
            ["oracle"] =
            [
                F("Pooling", "启用连接池", defaultValue: "true"),
                F("Min Pool Size", "最小连接数", defaultValue: "5"),
                F("Max Pool Size", "最大连接数", defaultValue: "150"),
                F("Connection Timeout", "连接超时", defaultValue: "60")
            ],
            ["sqlite"] =
            [
                F("Cache", "缓存模式", defaultValue: "Shared"),
                F("Mode", "打开模式", defaultValue: "ReadWriteCreate"),
                F("Journal Mode", "日志模式", defaultValue: "WAL"),
                F("Synchronous", "同步模式", defaultValue: "Normal"),
                F("Foreign Keys", "外键约束", defaultValue: "true")
            ],
            ["dm"] =
            [
                F("SCHEMA", "Schema 名称", defaultValue: "myschema"),
                F("Pooling", "启用连接池", defaultValue: "true"),
                F("Min Pool Size", "最小连接数", defaultValue: "1"),
                F("Max Pool Size", "最大连接数", defaultValue: "100"),
                F("Connection Timeout", "连接超时", defaultValue: "30"),
                F("DatabaseModel", "Docker/MySQL 兼容", defaultValue: "MySql")
            ],
            ["goldendb"] = [F("Pooling", "启用连接池", defaultValue: "false")],
            ["hg"] = [F("searchpath", "Schema 搜索路径"), F("Pooling", "启用连接池", defaultValue: "false")],
            ["kdbndp"] = KdbndpPerformance,
            ["kingbase"] = KdbndpPerformance,
            ["vastbase"] = [F("No Reset On Close", "关闭时不重置", defaultValue: "true")],
            // DatabaseSetting 无 Doris.md；按 MySQL 兼容习惯保留池化项。
            ["doris"] = [F("Charset", "字符集", defaultValue: "utf8mb4"), F("Pooling", "启用连接池", defaultValue: "true")]
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<Field>> Optional =
        new Dictionary<string, IReadOnlyList<Field>>(StringComparer.OrdinalIgnoreCase)
        {
            ["mysql"] = MysqlOptional,
            ["polardb"] = MysqlOptional,
            ["tidb"] =
            [
                F("tidb_txn_mode", "事务模式", defaultValue: "optimistic"),
                F("tidb_isolation_read_engines", "读取引擎", defaultValue: "tikv,tiflash,tidb")
            ],
            ["postgresql"] = PostgresOptional,
            ["opengauss"] = GaussDbOptional,
            ["gaussdb"] = GaussDbOptional,
            ["gaussdbnative"] = GaussDbOptional,
            ["sqlserver"] =
            [
                F("MultipleActiveResultSets", "多活动结果集", defaultValue: "false"),
                F("Application Name", "应用程序名称"),
                F("Integrated Security", "Windows 身份验证", defaultValue: "false")
            ],
            ["oracle"] =
            [
                F("Incr Pool Size", "连接池增量", defaultValue: "5"),
                F("Decr Pool Size", "连接池减量", defaultValue: "1"),
                F("Statement Cache Size", "语句缓存大小", defaultValue: "0")
            ],
            ["sqlite"] = [F("Password", "数据库密码", true)],
            ["questdb"] = [F("ServerCompatibilityMode", "兼容模式", defaultValue: "NoTypeLoading")],
            ["tdengine"] = [F("TsType", "时间精度")],
            ["dm"] =
            [
                F("PORT", "端口号（旧版）", defaultValue: "5236"),
                F("HOST", "主机地址（旧版）", defaultValue: "localhost"),
                F("Encrypt", "启用加密", defaultValue: "false")
            ],
            ["gbase"] = [F("Protocol", "协议", defaultValue: "onsoctcp"), F("Db_locale", "数据库区域"), F("Client_locale", "客户端区域")],
            ["kdbndp"] = KdbndpOptional,
            ["kingbase"] = KdbndpOptional
        };

    public static IReadOnlyList<Field> GetFields(string? dbType) =>
        !string.IsNullOrWhiteSpace(dbType) && Required.TryGetValue(dbType, out var fields) ? fields : Empty;

    public static IReadOnlyList<Field> GetPerformanceFields(string? dbType) =>
        !string.IsNullOrWhiteSpace(dbType) && Performance.TryGetValue(dbType, out var fields) ? fields : Empty;

    public static IReadOnlyList<Field> GetOptionalFields(string? dbType) =>
        !string.IsNullOrWhiteSpace(dbType) && Optional.TryGetValue(dbType, out var fields) ? fields : Empty;

    public static IReadOnlyList<Field> GetOptimizationFields(string? dbType) =>
        !string.IsNullOrWhiteSpace(dbType) && Optimization.TryGetValue(dbType, out var fields) ? fields : Empty;

    public static bool SupportsStructuredFields(string? dbType) => GetFields(dbType).Count > 0;

    public static bool IsRequiredKey(string? dbType, string key) =>
        GetFields(dbType).Any(field => string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase) || Aliases(field.Key).Any(alias => string.Equals(alias, key, StringComparison.OrdinalIgnoreCase)));

    public static bool IsPerformanceKey(string? dbType, string key) =>
        GetPerformanceFields(dbType).Any(field => string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase));

    public static bool IsOptionalKey(string? dbType, string key) =>
        GetOptionalFields(dbType).Any(field => string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase));

    public static string? GetValue(IReadOnlyDictionary<string, string> values, string key)
    {
        if (values.TryGetValue(key, out var value)) return value;
        foreach (var alias in Aliases(key))
        {
            if (values.TryGetValue(alias, out value)) return value;
        }
        return null;
    }

    private static IEnumerable<string> Aliases(string key) => key.ToLowerInvariant() switch
    {
        "user" => ["Uid", "UID", "User Id", "Username"],
        "uid" => ["User", "UID", "User Id", "Username"],
        "user id" => ["User", "Uid", "UID", "Username"],
        "username" => ["User", "Uid", "UID", "User Id"],
        "password" => ["Pwd", "PWD", "Password"],
        "pwd" => ["Password", "PASSWORD"],
        "server" => ["Host", "HOST"],
        "host" => ["Server", "HOST"],
        "database" => ["DATABASE", "database"],
        "data source" => ["DataSource"],
        "datasource" => ["Data Source"],
        _ => Array.Empty<string>()
    };

    public static string Assemble(string? dbType, IReadOnlyDictionary<string, string> values)
    {
        if (string.Equals(dbType, "mongodb", StringComparison.OrdinalIgnoreCase))
        {
            var user = GetValue(values, "Username") ?? "";
            var pwd = GetValue(values, "Password") ?? "";
            var host = GetValue(values, "Host") ?? "localhost";
            var port = GetValue(values, "Port") ?? "27017";
            var db = GetValue(values, "Database") ?? "";
            var authSource = GetValue(values, "authSource") ?? "";
            var credentials = string.IsNullOrEmpty(user) ? "" : string.IsNullOrEmpty(pwd) ? $"{Uri.EscapeDataString(user)}@" : $"{Uri.EscapeDataString(user)}:{Uri.EscapeDataString(pwd)}@";
            var query = string.IsNullOrEmpty(authSource) ? "" : $"?authSource={Uri.EscapeDataString(authSource)}";
            return $"mongodb://{credentials}{host}:{port}{(string.IsNullOrEmpty(db) ? "" : "/" + db)}{query}";
        }

        var ordered = new List<KeyValuePair<string, string>>();
        foreach (var field in GetFields(dbType))
        {
            var value = GetValue(values, field.Key);
            if (!string.IsNullOrEmpty(value)) ordered.Add(new(field.Key, value));
        }
        foreach (var option in GetPerformanceFields(dbType).Concat(GetOptionalFields(dbType)))
        {
            var value = GetValue(values, option.Key);
            if (!string.IsNullOrEmpty(value) && ordered.All(p => !string.Equals(p.Key, option.Key, StringComparison.OrdinalIgnoreCase))) ordered.Add(new(option.Key, value));
        }
        foreach (var pair in values)
        {
            if (!string.IsNullOrEmpty(pair.Value) && ordered.All(p => !string.Equals(p.Key, pair.Key, StringComparison.OrdinalIgnoreCase))) ordered.Add(pair);
        }
        return string.Join(';', ordered.Select(p => $"{p.Key}={p.Value}"));
    }

    public static IReadOnlyDictionary<string, string> Parse(string? dbType, string connectionString)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(connectionString)) return result;
        if (string.Equals(dbType, "mongodb", StringComparison.OrdinalIgnoreCase) && connectionString.StartsWith("mongodb://", StringComparison.OrdinalIgnoreCase))
        {
            var stripped = connectionString["mongodb://".Length..];
            var queryIndex = stripped.IndexOf('?');
            var query = queryIndex >= 0 ? stripped[(queryIndex + 1)..] : "";
            if (queryIndex >= 0) stripped = stripped[..queryIndex];
            var at = stripped.IndexOf('@');
            var hostInfo = at >= 0 ? stripped[(at + 1)..] : stripped;
            if (at >= 0)
            {
                var userInfo = stripped[..at]; var colon = userInfo.IndexOf(':');
                result["Username"] = Uri.UnescapeDataString(colon >= 0 ? userInfo[..colon] : userInfo);
                if (colon >= 0) result["Password"] = Uri.UnescapeDataString(userInfo[(colon + 1)..]);
            }
            var slash = hostInfo.IndexOf('/'); var hostPort = slash >= 0 ? hostInfo[..slash] : hostInfo;
            var database = slash >= 0 ? hostInfo[(slash + 1)..] : ""; var portColon = hostPort.LastIndexOf(':');
            result["Host"] = portColon >= 0 ? hostPort[..portColon] : hostPort;
            if (portColon >= 0) result["Port"] = hostPort[(portColon + 1)..];
            if (!string.IsNullOrEmpty(database)) result["Database"] = database;
            foreach (var item in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = item.IndexOf('='); if (eq > 0) result[Uri.UnescapeDataString(item[..eq])] = Uri.UnescapeDataString(item[(eq + 1)..]);
            }
            return result;
        }
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq > 0) result[part[..eq].Trim()] = part[(eq + 1)..].Trim();
        }
        return result;
    }
}


