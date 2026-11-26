using DatabaseMcpServer.Models;
using SqlSugar;

namespace DatabaseMcpServer.Helpers;

internal static class NativeObjectCatalog
{
    public static bool Supports(DbType provider) => ProviderCapabilities.Supports(provider, DatabaseCapability.NativeMetadata);

    public static void ValidateKind(DbType provider, string kind)
    {
        if (kind is not ("table" or "view" or "procedure" or "function" or "trigger"))
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "kind 必须为 table/view/procedure/function/trigger。");
        if (!Supports(provider) || provider == DbType.Sqlite && kind is "procedure" or "function")
            throw new DatabaseMcpException(DatabaseErrorCode.UnsupportedCapability, "此 provider 尚不支持该对象类型的元数据查询。");
    }

    public static string ListSql(DbType provider) => provider switch
    {
        DbType.MySql => """
            SELECT TABLE_SCHEMA AS `Schema`,CASE TABLE_TYPE WHEN 'VIEW' THEN 'view' ELSE 'table' END AS Kind,
                TABLE_NAME AS Name,NULL AS TableName,TABLE_NAME AS `Identity` FROM information_schema.TABLES
            WHERE TABLE_SCHEMA NOT IN ('mysql','information_schema','performance_schema','sys')
            UNION ALL SELECT ROUTINE_SCHEMA,lower(ROUTINE_TYPE),ROUTINE_NAME,NULL,ROUTINE_NAME FROM information_schema.ROUTINES
            WHERE ROUTINE_SCHEMA NOT IN ('mysql','information_schema','performance_schema','sys')
            UNION ALL SELECT TRIGGER_SCHEMA,'trigger',TRIGGER_NAME,EVENT_OBJECT_TABLE,TRIGGER_NAME FROM information_schema.TRIGGERS
            WHERE TRIGGER_SCHEMA NOT IN ('mysql','information_schema','performance_schema','sys')
            """,
        DbType.SqlServer => """
            SELECT s.name AS [Schema], CASE o.type WHEN 'U' THEN 'table' WHEN 'V' THEN 'view'
              WHEN 'P' THEN 'procedure' WHEN 'TR' THEN 'trigger' ELSE 'function' END AS Kind,
              o.name AS Name, OBJECT_NAME(o.parent_object_id) AS TableName, o.name AS [Identity]
            FROM sys.objects o JOIN sys.schemas s ON s.schema_id=o.schema_id
            WHERE o.is_ms_shipped=0 AND o.type IN ('U','V','P','TR','FN','IF','TF')
            """,
        DbType.PostgreSQL => """
            SELECT n.nspname AS "Schema", CASE WHEN c.relkind IN ('r','p') THEN 'table' ELSE 'view' END AS "Kind",
              c.relname AS "Name", NULL::text AS "TableName", c.relname AS "Identity"
            FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
            WHERE c.relkind IN ('r','p','v','m') AND n.nspname NOT IN ('pg_catalog','information_schema')
              AND n.nspname NOT LIKE 'pg_toast%' AND has_table_privilege(c.oid,'SELECT')
            UNION ALL
            SELECT n.nspname, CASE p.prokind WHEN 'p' THEN 'procedure' ELSE 'function' END, p.proname, NULL::text,
              quote_ident(p.proname) || '(' || pg_get_function_identity_arguments(p.oid) || ')'
            FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid=p.pronamespace
            WHERE p.prokind IN ('f','p') AND n.nspname NOT IN ('pg_catalog','information_schema') AND has_function_privilege(p.oid,'EXECUTE')
            UNION ALL
            SELECT n.nspname, 'trigger', t.tgname, c.relname, quote_ident(c.relname) || '.' || quote_ident(t.tgname)
            FROM pg_catalog.pg_trigger t JOIN pg_catalog.pg_class c ON c.oid=t.tgrelid
              JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace WHERE NOT t.tgisinternal AND has_table_privilege(c.oid,'SELECT')
            """,
        DbType.Sqlite => """
            SELECT 'main' AS "Schema", type AS Kind, name AS Name, tbl_name AS TableName, name AS "Identity"
            FROM main.sqlite_schema WHERE type IN ('table','view','trigger') AND name NOT LIKE 'sqlite_%'
            """,
        _ => throw new DatabaseMcpException(DatabaseErrorCode.UnsupportedCapability, "不支持该 provider 的对象目录。")
    };

    public static string DefinitionSql(DbType provider) => provider switch
    {
        DbType.SqlServer => """
            SELECT OBJECT_DEFINITION(o.object_id) AS Definition FROM sys.objects o
              JOIN sys.schemas s ON s.schema_id=o.schema_id
            WHERE s.name=@schema AND o.name=@name AND
              CASE o.type WHEN 'U' THEN 'table' WHEN 'V' THEN 'view' WHEN 'P' THEN 'procedure'
                WHEN 'TR' THEN 'trigger' WHEN 'FN' THEN 'function' WHEN 'IF' THEN 'function' WHEN 'TF' THEN 'function' END=@kind
            """,
        DbType.PostgreSQL => """
            SELECT pg_get_viewdef(c.oid,true) AS "Definition" FROM pg_catalog.pg_class c
              JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
            WHERE @kind='view' AND c.relkind IN ('v','m') AND n.nspname=@schema AND c.relname=@name AND has_table_privilege(c.oid,'SELECT')
            UNION ALL
            SELECT pg_get_functiondef(p.oid) FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid=p.pronamespace
            WHERE p.prokind IN ('f','p') AND n.nspname=@schema
              AND quote_ident(p.proname) || '(' || pg_get_function_identity_arguments(p.oid) || ')'=@name
              AND CASE p.prokind WHEN 'p' THEN 'procedure' ELSE 'function' END=@kind AND has_function_privilege(p.oid,'EXECUTE')
            UNION ALL
            SELECT pg_get_triggerdef(t.oid,true) FROM pg_catalog.pg_trigger t JOIN pg_catalog.pg_class c ON c.oid=t.tgrelid
              JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
            WHERE @kind='trigger' AND NOT t.tgisinternal AND n.nspname=@schema AND quote_ident(c.relname) || '.' || quote_ident(t.tgname)=@name
              AND has_table_privilege(c.oid,'SELECT')
            """,
        DbType.Sqlite => "SELECT sql AS Definition FROM main.sqlite_schema WHERE @schema='main' AND name=@name AND type=@kind",
        _ => throw new DatabaseMcpException(DatabaseErrorCode.UnsupportedCapability, "不支持该 provider 的对象定义。")
    };
}
