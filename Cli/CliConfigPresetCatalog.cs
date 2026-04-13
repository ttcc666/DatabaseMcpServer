namespace DatabaseMcpServer.Cli;

internal sealed record CliConfigPreset(
    string DbType,
    string ExampleName,
    string ExampleConnectionString,
    string Description);

internal static class CliConfigPresetCatalog
{
    public static IReadOnlyList<CliConfigPreset> Presets { get; } =
    [
        new("MySql", "mysql-main", "Server=localhost;Port=3306;Database=myapp;User=root;Password=123456;Charset=utf8mb4;AllowLoadLocalInfile=true;Min Pool Size=1;Max Pool Size=100;Allow User Variables=True;Pooling=true;", "MySQL 主库（utf8mb4 + 连接池 + BulkCopy）"),
        new("PostgreSQL", "postgres-analytics", "Host=localhost;Port=5432;Database=analytics;Username=postgres;Password=123456;Pooling=true;Minimum Pool Size=1;Maximum Pool Size=100;Timeout=30;Command Timeout=30;", "PostgreSQL 分析库（表名小写 + ILIKE）"),
        new("SqlServer", "sqlserver-crm", "Server=localhost;Database=crm;User Id=sa;Password=123456;Encrypt=True;TrustServerCertificate=True;Min Pool Size=1;Max Pool Size=100;", "SQL Server CRM（NoLock + 可禁用 nvarchar）"),
        new("Oracle", "oracle-erp", "Data Source=localhost/orcl;User ID=system;Password=oracle123;Pooling=true;Min Pool Size=5;Max Pool Size=150;", "Oracle ERP（大连接池 + 自增）"),
        new("Sqlite", "sqlite-local", "Data Source=./data/local.db;Cache=Shared;Mode=ReadWriteCreate;", "SQLite 本地库（共享缓存 + CodeFirst 增强）"),
        new("MongoDb", "mongodb-doc", "mongodb://root:123456@localhost:27017/mydb?authSource=admin", "MongoDB 文档库（单表 CRUD/分页/简单联表）"),
        new("ClickHouse", "clickhouse-olap", "Host=localhost;Port=8123;User=default;Password=;Database=default", "ClickHouse 分析库（列式/批量写入）"),
        new("Tidb", "tidb-bigdata", "Server=localhost;Port=4000;Database=bigdata;User=root;Password=123456;Charset=utf8mb4;Pooling=true;Min Pool Size=1;Max Pool Size=50;", "TiDB 分布式数据库（Hints + 悲观事务可选）"),
        new("OceanBase", "oceanbase-mysql", "Server=localhost;Port=2881;Database=test;User=root@sys;Password=password;Charset=utf8mb4;Pooling=true;", "OceanBase MySQL 模式（租户隔离 + 可选禁用连接池）"),
        new("OceanBaseForOracle", "oceanbase-oracle", "Driver={OceanBase ODBC 2.0 Driver};Server=172.19.9.9;Port=2883;Database=XIR_TRD;User=XIR_TRD@TENANT#CLUSTER:1650773680;Password=strong_pwd;Option=3;", "OceanBase Oracle 模式（ODBC + Oracle 兼容）"),
        new("Dm", "dm-finance", "Server=localhost;Port=5236;Database=finance;User=SYSDBA;Password=SYSDBA001;", "达梦数据库（Schema/Clob 优化）"),
        new("Kdbndp", "kdbndp-crm", "Server=localhost;Port=54321;Database=crm;User=SYSTEM;Password=system123;", "人大金仓（Oracle 兼容 + JSON/数组/游标）"),
        new("GaussDBNative", "gaussdb-analytics", "PORT=5432;DATABASE=analytics;HOST=localhost;PASSWORD=Gauss@123;USER ID=gaussdb;No Reset On Close=true;", "GaussDB 原生驱动（类型映射 + 批量）"),
        new("OpenGauss", "opengauss-tenant", "PORT=5432;DATABASE=tenant;HOST=localhost;PASSWORD=Gauss@123;USER ID=gaussdb;No Reset On Close=true;", "OpenGauss 租户库（PG 兼容）"),
        new("PolarDB", "polardb-app", "Server=localhost;Port=3306;Database=mydb;Uid=root;Pwd=123456;Pooling=false;", "PolarDB MySQL 兼容（建议禁用连接池）"),
        new("Vastbase", "vastbase-report", "PORT=5432;DATABASE=report;HOST=localhost;USER ID=postgres;PASSWORD=pass;No Reset On Close=true", "Vastbase 报表库（PG 兼容）"),
        new("HG", "highgo-app", "Server=27.151.1.54;Port=5866;UId=design;Password=000;Database=design;searchpath=design;Pooling=false;", "瀚高数据库（建议 Pooling=false）"),
        new("GoldenDB", "goldendb-core", "Server=localhost;Port=3306;Database=mydb;Uid=root;Pwd=123456;Pooling=false;", "GoldenDB（MySQL 兼容，必须 Pooling=false）"),
        new("GBase", "gbase-core", "Host=localhost;Service=19088;Server=gbase01;Database=testdb;Protocol=onsoctcp;Uid=gbasedbt;Pwd=GBase123;Db_locale=zh_CN.utf8;Client_locale=zh_CN.utf8", "GBase 8s（ODBC 驱动）"),
        new("Doris", "doris-olap", "Server=localhost;Database=mydb;Uid=root;Pwd=123456;Pooling=false;", "Doris OLAP（建议 Pooling=false）"),
        new("TDengine", "tdengine-iot", "Host=localhost;Port=6030;Username=root;Password=taosdata;Database=power", "TDengine 物联网库（ms/us/ns 精度，需 SDK）"),
        new("DuckDB", "duckdb-local", "DataSource=./duck.db", "DuckDB 本地 OLAP（嵌入式）"),
        new("QuestDB", "questdb-log", "host=localhost;port=8812;username=admin;password=quest;database=qdb;ServerCompatibilityMode=NoTypeLoading;", "QuestDB 时序库（追加写入 + 聚合）"),
        new("Oscar", "oscar-erp", "Data Source=localhost;User Id=sysdba;Password=oscar;", "神通数据库（Oracle 兼容）")
    ];

    public static bool TryGet(string dbType, out CliConfigPreset preset)
    {
        preset = Presets.FirstOrDefault(item => string.Equals(item.DbType, dbType, StringComparison.OrdinalIgnoreCase))!;
        return preset != null;
    }
}
