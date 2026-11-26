namespace DatabaseMcpServer.Configuration;

internal sealed record DatabasePreset(
    string DbType,
    string ExampleName,
    string ExampleConnectionString,
    string Description)
{
    public string Availability => UnavailableReason == null ? "available" : "unavailable";
    public string? UnavailableReason => Helpers.DatabaseProviderCatalog.UnavailableReason(Helpers.DatabaseProviderCatalog.Parse(DbType));
}

internal static class DatabasePresetCatalog
{
    public static IReadOnlyList<DatabasePreset> Presets { get; } =
    [
        new("MySql", "mysql-main", "Server=localhost;Port=3306;Database=myapp;User=root;Password=123456;Charset=utf8mb4;AllowLoadLocalInfile=true;Min Pool Size=1;Max Pool Size=100;Allow User Variables=True;Pooling=true;", "MySQL 主库（utf8mb4；池配置在连接串，未提供 BulkCopy 工具）"),
        new("MySqlConnector", "mysqlconnector-main", "Server=localhost;Port=3306;Database=myapp;User=root;Password=123456;Charset=utf8mb4;Pooling=true;", "MySQL（MySqlConnector 别名；与 MySql 同驱动，产品未单独实库验证）"),
        new("PostgreSQL", "postgres-analytics", "Host=localhost;Port=5432;Database=analytics;Username=postgres;Password=123456;Pooling=true;Minimum Pool Size=1;Maximum Pool Size=100;Timeout=30;Command Timeout=30;", "PostgreSQL 分析库（表名小写 + ILIKE）"),
        new("SqlServer", "sqlserver-crm", "Server=localhost;Database=crm;User Id=sa;Password=123456;Encrypt=True;TrustServerCertificate=True;Min Pool Size=1;Max Pool Size=100;", "SQL Server 本地开发（TrustServerCertificate=True；生产应验证证书）"),
        new("Oracle", "oracle-erp", "Data Source=localhost/orcl;User ID=system;Password=oracle123;Pooling=true;Min Pool Size=5;Max Pool Size=150;", "Oracle ERP（大连接池 + 自增）"),
        new("Sqlite", "sqlite-local", "Data Source=./data/local.db;Cache=Shared;Mode=ReadWriteCreate;", "SQLite 本地库（共享缓存 + CodeFirst 增强）"),
        new("MongoDb", "mongodb-doc", "mongodb://root:123456@localhost:27017/mydb?authSource=admin", "MongoDB（SqlSugar.MongoDbCore ADO 集合查询/批量读写与清空；URI 必须指定数据库）"),
        new("ClickHouse", "clickhouse-olap", "Host=localhost;Port=8123;User=default;Password=;Database=default", "ClickHouse 分析库（需按服务端验证，未提供 BulkCopy 工具）"),
        new("Tidb", "tidb-bigdata", "Server=localhost;Port=4000;Database=bigdata;User=root;Password=123456;Charset=utf8mb4;Pooling=true;Min Pool Size=1;Max Pool Size=50;", "TiDB MySQL 兼容驱动（产品未实库验收；Hints/事务开关不自动应用）"),
        new("TDSQL", "tdsql-mysql", "Server=localhost;Port=3306;Database=myapp;User=root;Password=123456;Charset=utf8mb4;Pooling=true;", "TDSQL MySQL 模式（SqlSugar 内置；产品未实库验收）"),
        new("OceanBase", "oceanbase-mysql", "Server=localhost;Port=2881;Database=test;User=root@sys;Password=password;Charset=utf8mb4;Pooling=true;", "OceanBase MySQL 模式（租户隔离 + 可选禁用连接池）"),
        new("OceanBaseForOracle", "oceanbase-oracle", "Driver={OceanBase ODBC 2.0 Driver};Server=localhost;Port=2883;Database=test;User=user@tenant#cluster;Password=strong_pwd;Option=3;", "OceanBase Oracle 模式（ODBC + Oracle 兼容；需安装 OceanBase ODBC 驱动）"),
        new("Dm", "dm-finance", "Server=localhost:5236;User Id=SYSDBA;PWD=SYSDBA001;DATABASE=finance;", "达梦数据库（ORM 大小写及模式设置；Schema 使用连接串）"),
        new("Kdbndp", "kdbndp-crm", "Server=localhost;Port=54321;Database=crm;User ID=SYSTEM;Password=system123;", "人大金仓（模式需匹配服务端；游标/原生数组工具未接入）"),
        new("GaussDB", "gaussdb-npgsql", "Host=localhost;Port=5432;Database=analytics;User ID=gaussdb;Password=Gauss@123;No Reset On Close=true;Pooling=true;", "GaussDB（Npgsql 兼容路径；建议 No Reset On Close=true）"),
        new("GaussDBNative", "gaussdb-native", "PORT=5432;DATABASE=analytics;HOST=localhost;PASSWORD=Gauss@123;USER ID=gaussdb;", "GaussDB 原生驱动（databaseModel 选择 GaussDB/OpenGauss）"),
        new("OpenGauss", "opengauss-tenant", "PORT=5432;DATABASE=tenant;HOST=localhost;PASSWORD=Gauss@123;USER ID=gaussdb;No Reset On Close=true;", "OpenGauss 租户库（PG 兼容）"),
        new("PolarDB", "polardb-app", "Server=localhost;Port=3306;Database=mydb;Uid=root;Pwd=123456;Pooling=false;", "PolarDB MySQL 兼容（建议禁用连接池）"),
        new("Vastbase", "vastbase-report", "PORT=5432;DATABASE=report;HOST=localhost;USER ID=postgres;PASSWORD=pass;No Reset On Close=true", "Vastbase 报表库（PG 兼容）"),
        new("HG", "highgo-app", "Server=localhost;Port=5866;UId=design;Password=000;Database=design;searchpath=design;Pooling=false;", "瀚高数据库（已包含 HGCore / Nhgdb 驱动）"),
        new("GoldenDB", "goldendb-core", "Server=localhost;Port=3306;Database=mydb;Uid=root;Pwd=123456;Pooling=false;", "GoldenDB（MySQL 兼容，必须 Pooling=false）"),
        new("GBase", "gbase-core", "Host=localhost;Service=19088;Server=gbase01;Database=testdb;Protocol=onsoctcp;Uid=gbasedbt;Pwd=GBase123;Db_locale=zh_CN.utf8;Client_locale=zh_CN.utf8", "GBase 8s（已包含 .NET 驱动；另需 ODBC/客户端 SDK）"),
        new("Doris", "doris-olap", "Server=localhost;Port=9030;Database=mydb;Uid=root;Pwd=123456;Pooling=false;", "Doris OLAP（FE 默认端口 9030；建议 Pooling=false）"),
        new("TDengine", "tdengine-iot", "Host=localhost;Port=6030;Username=root;Password=taosdata;Database=power", "TDengine（已包含 .NET 驱动；另需匹配原生 SDK）"),
        new("DuckDB", "duckdb-local", "DataSource=./duck.db", "DuckDB（已包含 .NET 驱动及支持平台的原生库）"),
        new("QuestDB", "questdb-log", "host=localhost;port=8812;username=admin;password=quest;database=qdb;ServerCompatibilityMode=NoTypeLoading;", "QuestDB 时序库（追加写入 + 聚合）"),
        new("Oscar", "oscar-erp", "HOST=localhost;PORT=2003;DATABASE=osrdb;USER ID=SYSDBA;PASSWORD=oscar;", "神通数据库（Oracle 兼容）")
    ];

    public static bool TryGet(string dbType, out DatabasePreset preset)
    {
        preset = Presets.FirstOrDefault(item => string.Equals(item.DbType, dbType, StringComparison.OrdinalIgnoreCase))!;
        return preset != null;
    }
}
