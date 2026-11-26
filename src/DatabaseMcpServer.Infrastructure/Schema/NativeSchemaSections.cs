using DatabaseMcpServer.Models;
using SqlSugar;

namespace DatabaseMcpServer.Helpers;

internal static class NativeSchemaSections
{
    internal sealed class Visibility { public bool? Value { get; set; } }

    public static async Task<object> ReadAsync(ISqlSugarClient db, string tableName, SchemaSectionKind section, BoundedQueryExecutor budget, Visibility visible, CancellationToken token)
    {
        var provider = db.CurrentConnectionConfig.DbType;
        if (ProviderCapabilities.SectionStatus(provider, section) == CapabilityStatus.Unsupported)
            return new { status = "unsupported", reason = "此 provider 未提供该 section。" };
        var parameters = NativeTableMetadata.Parameters(tableName, provider);
        var exists = provider switch
        {
            DbType.SqlServer => "SELECT COUNT(*) FROM sys.tables t JOIN sys.schemas s ON t.schema_id=s.schema_id WHERE s.name=@schema AND t.name=@name",
            DbType.PostgreSQL => "SELECT COUNT(*) FROM pg_class t JOIN pg_namespace s ON t.relnamespace=s.oid WHERE s.nspname=@schema AND t.relname=@name AND t.relkind IN ('r','p') AND has_table_privilege(t.oid,'SELECT')",
            DbType.MySql => "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=coalesce(@schema,DATABASE()) AND TABLE_NAME=@name",
            _ => "SELECT COUNT(*) FROM main.sqlite_schema WHERE type='table' AND name=@name"
        };
        visible.Value ??= Convert.ToInt64(await db.Ado.GetScalarAsync(exists, parameters)) > 0;
        token.ThrowIfCancellationRequested();
        if (!visible.Value.Value)
            return new { status = "unavailable", reason = "表不存在或当前角色不可见。" };
        if (section == SchemaSectionKind.ForeignKeys)
        {
            var rows = await budget.ReadMetadataAsync<ForeignKeyRow>(db, ForeignKeySql(provider), parameters, token);
            return new { status = "available", data = rows.GroupBy(r => r.Id).Select(g => new
            {
                name = g.First().Name, referencedSchema = g.First().ReferencedSchema, referencedTable = g.First().ReferencedTable,
                onUpdate = g.First().OnUpdate, onDelete = g.First().OnDelete,
                columns = g.OrderBy(r => r.Ordinal).Select(r => new { ordinal = r.Ordinal, sourceColumn = r.SourceColumn, targetColumn = r.TargetColumn }).ToArray()
            }).ToArray() };
        }
        if (section == SchemaSectionKind.Constraints)
        {
            var rows = await budget.ReadMetadataAsync<ConstraintRow>(db, ConstraintSql(provider), parameters, token);
            return new { status = ProviderCapabilities.SectionStatus(provider, section) == CapabilityStatus.Partial ? CapabilityStatus.Partial.Name() : "available",
                reason = provider == DbType.Sqlite ? "PRAGMA 提供主键/唯一键列顺序；无法无损恢复约束名称/CHECK，外键请请求 foreignKeys。" : null,
                data = rows.GroupBy(r => r.Id).Select(g => new { name = g.First().Name, kind = g.First().Kind, definition = g.First().Definition,
                    columns = g.Where(r => r.ColumnName != null).OrderBy(r => r.Ordinal).Select(r => new { ordinal = r.Ordinal, columnName = r.ColumnName }).ToArray() }).ToArray() };
        }
        return new { status = "available", data = await budget.ReadMetadataAsync<CommentRow>(db, CommentSql(provider), parameters, token) };
    }

    private sealed class ForeignKeyRow
    {
        public string Id { get; set; } = "";
        public string? Name { get; set; }
        public string ReferencedSchema { get; set; } = "";
        public string ReferencedTable { get; set; } = "";
        public string OnUpdate { get; set; } = "";
        public string OnDelete { get; set; } = "";
        public int Ordinal { get; set; }
        public string SourceColumn { get; set; } = "";
        public string? TargetColumn { get; set; }
    }
    private sealed class ConstraintRow
    {
        public string Id { get; set; } = "";
        public string? Name { get; set; }
        public string Kind { get; set; } = "";
        public string? Definition { get; set; }
        public string? ColumnName { get; set; }
        public int Ordinal { get; set; }
    }
    private sealed class CommentRow
    {
        public string? ColumnName { get; set; }
        public string? Comment { get; set; }
    }

    private static string ForeignKeySql(DbType provider) => provider switch
    {
        DbType.MySql => """
            SELECT k.CONSTRAINT_NAME AS Id,k.CONSTRAINT_NAME AS Name,k.REFERENCED_TABLE_SCHEMA AS ReferencedSchema,
                k.REFERENCED_TABLE_NAME AS ReferencedTable,r.UPDATE_RULE AS OnUpdate,r.DELETE_RULE AS OnDelete,
                k.ORDINAL_POSITION AS Ordinal,k.COLUMN_NAME AS SourceColumn,k.REFERENCED_COLUMN_NAME AS TargetColumn
            FROM information_schema.KEY_COLUMN_USAGE k JOIN information_schema.REFERENTIAL_CONSTRAINTS r
                ON r.CONSTRAINT_SCHEMA=k.CONSTRAINT_SCHEMA AND r.CONSTRAINT_NAME=k.CONSTRAINT_NAME AND r.TABLE_NAME=k.TABLE_NAME
            WHERE k.TABLE_SCHEMA=coalesce(@schema,DATABASE()) AND k.TABLE_NAME=@name AND k.REFERENCED_TABLE_NAME IS NOT NULL
            ORDER BY k.CONSTRAINT_NAME,k.ORDINAL_POSITION
            """,
        DbType.SqlServer => """
            SELECT CONVERT(varchar(20),f.object_id) AS Id,f.name AS Name,rs.name AS ReferencedSchema,rt.name AS ReferencedTable,
              f.update_referential_action_desc AS OnUpdate,f.delete_referential_action_desc AS OnDelete,
              fc.constraint_column_id AS Ordinal,sc.name AS SourceColumn,rc.name AS TargetColumn
            FROM sys.foreign_keys f JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id
              JOIN sys.tables st ON st.object_id=f.parent_object_id JOIN sys.schemas ss ON ss.schema_id=st.schema_id
              JOIN sys.tables rt ON rt.object_id=f.referenced_object_id JOIN sys.schemas rs ON rs.schema_id=rt.schema_id
              JOIN sys.columns sc ON sc.object_id=st.object_id AND sc.column_id=fc.parent_column_id
              JOIN sys.columns rc ON rc.object_id=rt.object_id AND rc.column_id=fc.referenced_column_id
            WHERE ss.name=@schema AND st.name=@name ORDER BY f.object_id,fc.constraint_column_id
            """,
        DbType.PostgreSQL => """
            SELECT f.oid::text AS "Id",f.conname AS "Name",rs.nspname AS "ReferencedSchema",rt.relname AS "ReferencedTable",
              CASE f.confupdtype WHEN 'a' THEN 'NO ACTION' WHEN 'r' THEN 'RESTRICT' WHEN 'c' THEN 'CASCADE' WHEN 'n' THEN 'SET NULL' ELSE 'SET DEFAULT' END AS "OnUpdate",
              CASE f.confdeltype WHEN 'a' THEN 'NO ACTION' WHEN 'r' THEN 'RESTRICT' WHEN 'c' THEN 'CASCADE' WHEN 'n' THEN 'SET NULL' ELSE 'SET DEFAULT' END AS "OnDelete",
              k.ordinality::int AS "Ordinal",sc.attname AS "SourceColumn",rc.attname AS "TargetColumn"
            FROM pg_constraint f JOIN pg_class st ON st.oid=f.conrelid JOIN pg_namespace ss ON ss.oid=st.relnamespace
              JOIN pg_class rt ON rt.oid=f.confrelid JOIN pg_namespace rs ON rs.oid=rt.relnamespace
              CROSS JOIN LATERAL unnest(f.conkey,f.confkey) WITH ORDINALITY k(src,dst,ordinality)
              JOIN pg_attribute sc ON sc.attrelid=st.oid AND sc.attnum=k.src JOIN pg_attribute rc ON rc.attrelid=rt.oid AND rc.attnum=k.dst
            WHERE f.contype='f' AND ss.nspname=@schema AND st.relname=@name ORDER BY f.oid,k.ordinality
            """,
        _ => """
            SELECT CAST(f.id AS TEXT) AS Id,NULL AS Name,'main' AS ReferencedSchema,f."table" AS ReferencedTable,
              f.on_update AS OnUpdate,f.on_delete AS OnDelete,f.seq+1 AS Ordinal,f."from" AS SourceColumn,coalesce(f."to",p.name) AS TargetColumn
            FROM pragma_foreign_key_list(@name) f LEFT JOIN pragma_table_info(f."table") p ON p.pk=f.seq+1 ORDER BY f.id,f.seq
            """
    };

    private static string ConstraintSql(DbType provider) => provider switch
    {
        DbType.MySql => """
            SELECT t.CONSTRAINT_NAME AS Id,t.CONSTRAINT_NAME AS Name,
                CASE t.CONSTRAINT_TYPE WHEN 'PRIMARY KEY' THEN 'primary-key' WHEN 'UNIQUE' THEN 'unique' ELSE 'check' END AS Kind,
                c.CHECK_CLAUSE AS Definition,k.COLUMN_NAME AS ColumnName,coalesce(k.ORDINAL_POSITION,0) AS Ordinal
            FROM information_schema.TABLE_CONSTRAINTS t LEFT JOIN information_schema.KEY_COLUMN_USAGE k
                ON k.CONSTRAINT_SCHEMA=t.CONSTRAINT_SCHEMA AND k.CONSTRAINT_NAME=t.CONSTRAINT_NAME AND k.TABLE_NAME=t.TABLE_NAME
            LEFT JOIN information_schema.CHECK_CONSTRAINTS c ON c.CONSTRAINT_SCHEMA=t.CONSTRAINT_SCHEMA AND c.CONSTRAINT_NAME=t.CONSTRAINT_NAME
            WHERE t.TABLE_SCHEMA=coalesce(@schema,DATABASE()) AND t.TABLE_NAME=@name AND t.CONSTRAINT_TYPE IN ('PRIMARY KEY','UNIQUE','CHECK')
            ORDER BY t.CONSTRAINT_NAME,k.ORDINAL_POSITION
            """,
        DbType.SqlServer => """
            SELECT CONVERT(varchar(20),kc.object_id) AS Id,kc.name AS Name,CASE kc.type WHEN 'PK' THEN 'primary-key' ELSE 'unique' END AS Kind,
              NULL AS Definition,c.name AS ColumnName,ic.key_ordinal AS Ordinal
            FROM sys.key_constraints kc JOIN sys.tables t ON t.object_id=kc.parent_object_id JOIN sys.schemas s ON s.schema_id=t.schema_id
              JOIN sys.index_columns ic ON ic.object_id=t.object_id AND ic.index_id=kc.unique_index_id AND ic.key_ordinal>0
              JOIN sys.columns c ON c.object_id=t.object_id AND c.column_id=ic.column_id WHERE s.name=@schema AND t.name=@name
            UNION ALL SELECT CONVERT(varchar(20),cc.object_id),cc.name,'check',cc.definition,NULL,0
            FROM sys.check_constraints cc JOIN sys.tables t ON t.object_id=cc.parent_object_id JOIN sys.schemas s ON s.schema_id=t.schema_id WHERE s.name=@schema AND t.name=@name
            UNION ALL SELECT CONVERT(varchar(20),dc.object_id),dc.name,'default',dc.definition,c.name,1
            FROM sys.default_constraints dc JOIN sys.tables t ON t.object_id=dc.parent_object_id JOIN sys.schemas s ON s.schema_id=t.schema_id
              JOIN sys.columns c ON c.object_id=t.object_id AND c.column_id=dc.parent_column_id WHERE s.name=@schema AND t.name=@name
            """,
        DbType.PostgreSQL => """
            SELECT f.oid::text AS "Id",f.conname AS "Name",CASE f.contype WHEN 'p' THEN 'primary-key' WHEN 'u' THEN 'unique' ELSE 'check' END AS "Kind",
              pg_get_constraintdef(f.oid,true) AS "Definition",c.attname AS "ColumnName",coalesce(k.ordinality,0)::int AS "Ordinal"
            FROM pg_constraint f JOIN pg_class t ON t.oid=f.conrelid JOIN pg_namespace s ON s.oid=t.relnamespace
              LEFT JOIN LATERAL unnest(f.conkey) WITH ORDINALITY k(col,ordinality) ON true
              LEFT JOIN pg_attribute c ON c.attrelid=t.oid AND c.attnum=k.col
            WHERE f.contype IN ('p','u','c') AND s.nspname=@schema AND t.relname=@name
            """,
        _ => """
            SELECT 'pk' AS Id,NULL AS Name,'primary-key' AS Kind,NULL AS Definition,name AS ColumnName,pk AS Ordinal FROM pragma_table_info(@name) WHERE pk>0
            UNION ALL SELECT i.name,NULL,'unique',NULL,c.name,c.seqno+1 FROM pragma_index_list(@name) i JOIN pragma_index_info(i.name) c WHERE i.origin='u'
            """
    };

    private static string CommentSql(DbType provider) => provider == DbType.MySql ? """
        SELECT NULL AS ColumnName,TABLE_COMMENT AS Comment FROM information_schema.TABLES WHERE TABLE_SCHEMA=coalesce(@schema,DATABASE()) AND TABLE_NAME=@name
        UNION ALL SELECT COLUMN_NAME,COLUMN_COMMENT FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=coalesce(@schema,DATABASE()) AND TABLE_NAME=@name
        """ : provider == DbType.SqlServer ? """
        SELECT c.name AS ColumnName,CONVERT(nvarchar(max),e.value) AS Comment
        FROM sys.extended_properties e JOIN sys.tables t ON t.object_id=e.major_id JOIN sys.schemas s ON s.schema_id=t.schema_id
          LEFT JOIN sys.columns c ON c.object_id=t.object_id AND c.column_id=e.minor_id
        WHERE e.class=1 AND e.name='MS_Description' AND s.name=@schema AND t.name=@name
        """ : """
        SELECT NULL::text AS "ColumnName",obj_description(t.oid,'pg_class') AS "Comment" FROM pg_class t JOIN pg_namespace s ON s.oid=t.relnamespace WHERE s.nspname=@schema AND t.relname=@name
        UNION ALL SELECT c.attname,col_description(t.oid,c.attnum) FROM pg_attribute c JOIN pg_class t ON t.oid=c.attrelid JOIN pg_namespace s ON s.oid=t.relnamespace
        WHERE s.nspname=@schema AND t.relname=@name AND c.attnum>0 AND NOT c.attisdropped
        """;
}
