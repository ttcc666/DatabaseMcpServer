using DatabaseMcpServer.Models;
using SqlSugar;

namespace DatabaseMcpServer.Helpers;

internal static class NativeTableMetadata
{
    internal sealed class Column
    {
        public string Name { get; set; } = "";
        public string DataType { get; set; } = "";
        public int Ordinal { get; set; }
        public int PrimaryKeyOrdinal { get; set; }
        public int NullableFlag { get; set; }
        public int IdentityFlag { get; set; }
        public string? DefaultValue { get; set; }
        public object Describe() => new { name = Name, dataType = DataType, ordinal = Ordinal,
            primaryKeyOrdinal = PrimaryKeyOrdinal, nullable = NullableFlag != 0, identity = IdentityFlag != 0, defaultValue = DefaultValue };
    }

    public static (string? Schema, string Name) Parse(string tableName, DbType provider)
    {
        if (string.IsNullOrWhiteSpace(tableName) || tableName.Length > 1024 || tableName.Contains('\0'))
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "tableName 无效。");
        var parts = tableName.Split('.', 2);
        var schema = parts.Length == 2 ? parts[0] : provider switch
        { DbType.SqlServer => "dbo", DbType.PostgreSQL => "public", DbType.MySql => null, _ => "main" };
        if (provider == DbType.Sqlite && schema != "main")
            throw new DatabaseMcpException(DatabaseErrorCode.UnsupportedCapability, "仅支持 main schema。");
        return (schema, parts[^1]);
    }

    public static SugarParameter[] Parameters(string tableName, DbType provider)
    {
        var (schema, name) = Parse(tableName, provider);
        return [new("@schema", schema) { DbType = System.Data.DbType.String }, new("@name", name)];
    }

    public static Task<List<Column>> ColumnsAsync(ISqlSugarClient db, string table, BoundedQueryExecutor budget, CancellationToken token)
        => budget.ReadMetadataAsync<Column>(db, ColumnsSql(db.CurrentConnectionConfig.DbType), Parameters(table, db.CurrentConnectionConfig.DbType), token);

    public static string ColumnsSql(DbType provider) => provider switch
    {
        DbType.Sqlite => """
            WITH columns AS (
                SELECT *, pk=1 AND upper(type)='INTEGER'
                    AND NOT EXISTS(SELECT 1 FROM pragma_index_list(@name) WHERE origin='pk') AS RowidAlias
                FROM pragma_table_xinfo(@name)
            )
            SELECT name AS Name,type AS DataType,cid+1 AS Ordinal,pk AS PrimaryKeyOrdinal,
                CASE WHEN "notnull"=1 OR RowidAlias THEN 0 ELSE 1 END AS NullableFlag,
                CASE WHEN RowidAlias THEN 1 ELSE 0 END AS IdentityFlag,
                dflt_value AS DefaultValue FROM columns ORDER BY cid
            """,
        DbType.SqlServer => """
            SELECT c.name AS Name,TYPE_NAME(c.user_type_id) AS DataType,c.column_id AS Ordinal,
                coalesce(pk.key_ordinal,0) AS PrimaryKeyOrdinal,CONVERT(int,c.is_nullable) AS NullableFlag,
                CONVERT(int,c.is_identity) AS IdentityFlag,OBJECT_DEFINITION(c.default_object_id) AS DefaultValue
            FROM sys.columns c JOIN sys.objects t ON c.object_id=t.object_id JOIN sys.schemas s ON t.schema_id=s.schema_id
            LEFT JOIN (SELECT ic.object_id,ic.column_id,ic.key_ordinal FROM sys.index_columns ic JOIN sys.indexes i
                ON i.object_id=ic.object_id AND i.index_id=ic.index_id WHERE i.is_primary_key=1) pk
                ON pk.object_id=t.object_id AND pk.column_id=c.column_id
            WHERE s.name=@schema AND t.name=@name AND t.type IN ('U','V') ORDER BY c.column_id
            """,
        DbType.PostgreSQL => """
            SELECT c.attname AS "Name",format_type(c.atttypid,c.atttypmod) AS "DataType",c.attnum::int AS "Ordinal",
                coalesce(array_position(pk.conkey,c.attnum),0)::int AS "PrimaryKeyOrdinal",
                CASE WHEN c.attnotnull THEN 0 ELSE 1 END AS "NullableFlag",
                CASE WHEN c.attidentity<>'' OR pg_get_expr(d.adbin,d.adrelid) LIKE 'nextval(%' THEN 1 ELSE 0 END AS "IdentityFlag",
                pg_get_expr(d.adbin,d.adrelid) AS "DefaultValue"
            FROM pg_attribute c JOIN pg_class t ON t.oid=c.attrelid JOIN pg_namespace s ON s.oid=t.relnamespace
            LEFT JOIN pg_constraint pk ON pk.conrelid=t.oid AND pk.contype='p'
            LEFT JOIN pg_attrdef d ON d.adrelid=t.oid AND d.adnum=c.attnum
            WHERE s.nspname=@schema AND t.relname=@name AND c.attnum>0 AND NOT c.attisdropped AND has_table_privilege(t.oid,'SELECT') ORDER BY c.attnum
            """,
        DbType.MySql => """
            SELECT c.COLUMN_NAME AS Name,c.COLUMN_TYPE AS DataType,c.ORDINAL_POSITION AS Ordinal,
                coalesce(k.ORDINAL_POSITION,0) AS PrimaryKeyOrdinal,CASE c.IS_NULLABLE WHEN 'YES' THEN 1 ELSE 0 END AS NullableFlag,
                CASE WHEN c.EXTRA LIKE '%auto_increment%' THEN 1 ELSE 0 END AS IdentityFlag,c.COLUMN_DEFAULT AS DefaultValue
            FROM information_schema.COLUMNS c LEFT JOIN information_schema.KEY_COLUMN_USAGE k
                ON k.TABLE_SCHEMA=c.TABLE_SCHEMA AND k.TABLE_NAME=c.TABLE_NAME AND k.COLUMN_NAME=c.COLUMN_NAME AND k.CONSTRAINT_NAME='PRIMARY'
            WHERE c.TABLE_SCHEMA=coalesce(@schema,DATABASE()) AND c.TABLE_NAME=@name ORDER BY c.ORDINAL_POSITION
            """,
        DbType.DuckDB => """
            SELECT c.column_name AS Name,c.data_type AS DataType,c.ordinal_position AS Ordinal,
                coalesce(k.ordinal_position,0) AS PrimaryKeyOrdinal,CASE c.is_nullable WHEN 'YES' THEN 1 ELSE 0 END AS NullableFlag,
                0 AS IdentityFlag,c.column_default AS DefaultValue
            FROM information_schema.columns c LEFT JOIN (SELECT k.* FROM information_schema.key_column_usage k
                JOIN information_schema.table_constraints t USING(constraint_catalog,constraint_schema,constraint_name) WHERE t.constraint_type='PRIMARY KEY') k
                ON k.table_schema=c.table_schema AND k.table_name=c.table_name AND k.column_name=c.column_name
            WHERE c.table_schema=@schema AND c.table_name=@name ORDER BY c.ordinal_position
            """,
        _ => throw new DatabaseMcpException(DatabaseErrorCode.UnsupportedCapability, "此 provider 尚未提供异步字段元数据。")
    };

    public static string IndexesSql(DbType provider) => provider switch
    {
        DbType.Sqlite => """SELECT name AS name,"unique" AS isUnique,origin AS origin,partial AS isPartial FROM pragma_index_list(@name) ORDER BY name""",
        DbType.MySql => """
            SELECT INDEX_NAME AS name,COLUMN_NAME AS columnName,SEQ_IN_INDEX AS ordinal,1-NON_UNIQUE AS isUnique,INDEX_TYPE AS type
            FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=coalesce(@schema,DATABASE()) AND TABLE_NAME=@name ORDER BY INDEX_NAME,SEQ_IN_INDEX
            """,
        DbType.SqlServer => """
            SELECT i.name AS name,c.name AS columnName,ic.key_ordinal AS ordinal,CONVERT(int,i.is_unique) AS isUnique,
                CONVERT(int,ic.is_included_column) AS isIncluded,i.filter_definition AS filter
            FROM sys.indexes i JOIN sys.tables t ON i.object_id=t.object_id JOIN sys.schemas s ON s.schema_id=t.schema_id
                JOIN sys.index_columns ic ON ic.object_id=t.object_id AND ic.index_id=i.index_id
                JOIN sys.columns c ON c.object_id=t.object_id AND c.column_id=ic.column_id
            WHERE s.name=@schema AND t.name=@name ORDER BY i.name,ic.index_column_id
            """,
        DbType.PostgreSQL => "SELECT indexname AS name,indexdef AS definition FROM pg_indexes WHERE schemaname=@schema AND tablename=@name ORDER BY indexname",
        DbType.DuckDB => "SELECT index_name AS name,sql AS definition FROM duckdb_indexes() WHERE schema_name=@schema AND table_name=@name ORDER BY index_name",
        _ => throw new DatabaseMcpException(DatabaseErrorCode.UnsupportedCapability, "此 provider 尚未提供异步索引元数据。")
    };
}
