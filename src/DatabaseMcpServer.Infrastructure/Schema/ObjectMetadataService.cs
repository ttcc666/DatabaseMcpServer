using System.Text.Json;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Services;

internal sealed class ObjectMetadataService(IDatabaseConfigService config, IDatabaseHelperService helper,
    ILogger<ObjectMetadataService> logger, DatabaseExecutionOptions options, DatabaseMetadataCache cache, SqlDatabaseSessions sessions)
    : SqlOperationExecutor(config, helper, logger, options, cache, sessions)
{
    private sealed record Cursor(string Binding, int Offset, string[]? Keys = null);
    private static readonly JsonSerializerOptions MetadataJson = new() { PropertyNameCaseInsensitive = true };

    public Task<object> ListObjects(string kind, string? databaseName, string? schema, string? namePrefix,
        string? tableName, string? cursor, int limit, int? maxResponseBytes, int? commandTimeoutSeconds, CancellationToken token)
        => WithDeadlineAsync(async (context, token) =>
        {
            var provider = context.Client.CurrentConnectionConfig.DbType;
            var product = DatabaseProviderCatalog.Parse(context.DatabaseType);
            var native = NativeObjectCatalog.Supports(product);
            if (kind is not ("table" or "view" or "procedure" or "function" or "trigger"))
                throw Invalid("kind 必须为 table/view/procedure/function/trigger。");
            if (native) NativeObjectCatalog.ValidateKind(provider, kind);
            else if (kind is "procedure" or "function") ProviderCapabilities.Require(product, DatabaseCapability.Routines);
            if (limit is < 1 or > 1000) throw Invalid("limit 必须在 1-1000 之间。");
            var limits = ExecutionOptions.ResolveQuery(limit, maxResponseBytes, commandTimeoutSeconds, bounded: true);
            limit = limits.Rows!.Value;
            ValidateName(schema); ValidateName(namePrefix); ValidateName(tableName);
            if (provider == DbType.Sqlite && schema is not (null or "main"))
                throw new DatabaseMcpException(DatabaseErrorCode.UnsupportedCapability, "SQLite 对象目录目前仅支持 main schema。");
            var binding = KeysetCursor.Bind(context, new { kind, schema, namePrefix, tableName, limit });
            var offset = 0;
            string[]? keys = null;
            if (cursor != null)
            {
                try
                {
                    if (cursor.Length > 32768) throw Invalid("cursor 无效。");
                    var decoded = JsonSerializer.Deserialize<Cursor>(Convert.FromBase64String(cursor));
                    if (decoded == null || decoded.Binding != binding || decoded.Offset < 0 || decoded.Offset > int.MaxValue - limit)
                        throw Invalid("cursor 与当前连接版本或查询条件不匹配。");
                    offset = decoded.Offset;
                    keys = decoded.Keys;
                    if (native && (keys == null || keys.Length != 4 || keys.Any(k => k == null)))
                        throw Invalid("cursor 缺少排序键。");
                }
                catch (Exception ex) when (ex is FormatException or JsonException) { throw Invalid("cursor 无效。"); }
            }
            if (!native)
            {
                if (schema != null) throw new DatabaseMcpException(DatabaseErrorCode.UnsupportedCapability,
                    "此 provider 的 SqlSugar 目录不提供可靠 schema；请省略 schema，结果明确标记为 client paging。");
                token.ThrowIfCancellationRequested();
                var fallbackObjects = ProviderObjectCatalog.Read(context.Client, kind, tableName)
                    .Where(item => namePrefix == null || item.Name.StartsWith(namePrefix, StringComparison.Ordinal))
                    .OrderBy(item => item.Name, StringComparer.Ordinal).ThenBy(item => item.Identity, StringComparer.Ordinal)
                    .Skip(offset).Take(limit + 1).ToList();
                token.ThrowIfCancellationRequested();
                var fallbackMore = fallbackObjects.Count > limit;
                if (fallbackMore) fallbackObjects.RemoveAt(limit);
                return BoundedQueryExecutor.EncodeResponse(new ObjectListResult
                {
                    Success = true,
                    DatabaseName = context.DatabaseName,
                    ConfigurationVersion = context.ConfigurationVersion,
                    Objects = fallbackObjects,
                    PagingMode = "client",
                    Consistency = "SqlSugar provider metadata; schema unavailable; no cross-page snapshot",
                    NextCursor = fallbackMore ? Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new Cursor(binding, offset + limit))) : null
                }, limits.Bytes!.Value);
            }
            Func<string, string> quote = s => provider switch { DbType.SqlServer => $"[{s}]", DbType.MySql => $"`{s}`", _ => $"\"{s}\"" };
            string SortKey(string column) => provider switch
            {
                DbType.MySql => $"CAST({quote(column)} AS BINARY)",
                DbType.SqlServer => $"{quote(column)} COLLATE Latin1_General_100_BIN2",
                DbType.PostgreSQL => $"{quote(column)} COLLATE \"C\"",
                _ => quote(column)
            };
            var prefixFilter = provider == DbType.SqlServer
                ? $"LEFT({quote("Name")},LEN(@prefix + '#')-1)=@prefix COLLATE Latin1_General_100_BIN2"
                : provider == DbType.PostgreSQL ? $"left({quote("Name")},length(@prefix))=@prefix" : provider == DbType.MySql
                    ? $"CAST(left({quote("Name")},char_length(@prefix)) AS BINARY)=CAST(@prefix AS BINARY)" : $"substr({quote("Name")},1,length(@prefix))=@prefix";
            var sql = $"SELECT * FROM ({NativeObjectCatalog.ListSql(provider)}) catalog WHERE {quote("Kind")}=@kind" +
                (schema == null ? "" : $" AND {quote("Schema")}=@schema") +
                (namePrefix == null ? "" : $" AND {prefixFilter}") +
                (tableName == null ? "" : $" AND {quote("TableName")}=@tableName") +
                (keys == null ? "" : " AND (" + KeysetCursor.Predicate(new[] { "Schema", "Kind", "Name", "Identity" }.Select(SortKey).ToArray()) + ")") +
                $" ORDER BY {SortKey("Schema")},{SortKey("Kind")},{SortKey("Name")},{SortKey("Identity")}" +
                (provider == DbType.SqlServer ? " OFFSET 0 ROWS FETCH NEXT @take ROWS ONLY" : " LIMIT @take");
            var parameters = new List<SugarParameter> { new("@kind", kind), new("@take", limit + 1) };
            if (keys != null) parameters.AddRange(keys.Select((key, i) => new SugarParameter("@seek" + i, key)));
            if (schema != null) parameters.Add(new("@schema", schema));
            if (namePrefix != null) parameters.Add(new("@prefix", namePrefix));
            if (tableName != null) parameters.Add(new("@tableName", tableName));
            var budget = new BoundedQueryExecutor(limit + 2, limits.Bytes, 256);
            var rows = await budget.QueryResultAsync(context, sql, parameters.ToArray(), token);
            if (rows.LimitReached) throw new DatabaseMcpException(DatabaseErrorCode.ResponseLimitExceeded, "对象目录超出预算；请缩小 limit 或前缀范围。");
            var objects = rows.Data.Select(row => JsonSerializer.Deserialize<DatabaseObjectInfo>(row.Utf8Json, MetadataJson)!).ToList();
            var more = objects.Count > limit;
            if (more) objects.RemoveAt(limit);
            return (object)BoundedQueryExecutor.EncodeResponse(new ObjectListResult
            {
                Success = true,
                DatabaseName = context.DatabaseName,
                ConfigurationVersion = context.ConfigurationVersion,
                Objects = objects,
                PagingMode = "keyset",
                NextCursor = more ? Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(
                    new Cursor(binding, 0, [objects[^1].Schema!, objects[^1].Kind, objects[^1].Name, objects[^1].Identity!]))) : null
            }, limits.Bytes!.Value);
        }, commandTimeoutSeconds, token, databaseName);

    public Task<object> GetDefinition(string kind, string name, string? schema, string? databaseName,
        int? maxResponseBytes, int? commandTimeoutSeconds, CancellationToken token)
        => WithDeadlineAsync(async (context, token) =>
        {
            ProviderCapabilities.Require(DatabaseProviderCatalog.Parse(context.DatabaseType), DatabaseCapability.NativeMetadata);
            var provider = context.Client.CurrentConnectionConfig.DbType;
            NativeObjectCatalog.ValidateKind(provider, kind);
            ValidateName(name); ValidateName(schema);
            if (string.IsNullOrWhiteSpace(name)) throw Invalid("name 不能为空。");
            var limits = ExecutionOptions.ResolveQuery(1, maxResponseBytes, commandTimeoutSeconds, bounded: true);
            schema ??= provider switch { DbType.SqlServer => "dbo", DbType.PostgreSQL => "public", DbType.MySql => null, _ => "main" };
            if (provider == DbType.Sqlite && schema != "main") throw new DatabaseMcpException(DatabaseErrorCode.UnsupportedCapability, "仅支持 main schema。");
            var db = context.Client;
            if (provider == DbType.MySql)
            {
                schema ??= Convert.ToString(await db.Ado.GetScalarAsync("SELECT DATABASE()"));
                if (string.IsNullOrWhiteSpace(schema)) throw Invalid("请指定 schema 或在连接字符串中设置数据库。");
                static string Quote(string identifier) => "`" + identifier.Replace("`", "``", StringComparison.Ordinal) + "`";
                string? definitionText = null;
                try
                {
                    using var reader = await db.Ado.GetDataReaderAsync($"SHOW CREATE {kind.ToUpperInvariant()} {Quote(schema)}.{Quote(name)}");
                    if (await QueryReader.ReadAsync(reader, token))
                    {
                        var index = Enumerable.Range(0, reader.FieldCount).FirstOrDefault(i => reader.GetName(i).StartsWith("Create ", StringComparison.OrdinalIgnoreCase) ||
                            reader.GetName(i).Equals("SQL Original Statement", StringComparison.OrdinalIgnoreCase), -1);
                        if (index >= 0 && !reader.IsDBNull(index)) definitionText = reader.GetString(index);
                    }
                }
                catch (MySqlConnector.MySqlException ex) when (ex.Number is 1142 or 1146 or 1227 or 1305 or 1360)
                { /* Missing objects and restricted definitions share the same availability contract. */ }
                return BoundedQueryExecutor.EncodeResponse(new ObjectDefinitionResult
                {
                    Success = true,
                    DatabaseName = context.DatabaseName,
                    Kind = kind,
                    Name = name,
                    Schema = schema,
                    Definition = definitionText,
                    Availability = definitionText == null ? "definition-unavailable" : "available"
                }, limits.Bytes!.Value);
            }
            var column = provider == DbType.PostgreSQL ? "\"Definition\"" : "Definition";
            // Cap the value in the database before the driver materializes it.
            var expression = provider == DbType.SqlServer ? $"SUBSTRING({column},1,@characters)" : provider == DbType.PostgreSQL
                ? $"left({column},@characters)" : $"substr({column},1,@characters)";
            var definition = await db.Ado.GetScalarAsync($"SELECT {expression} FROM ({NativeObjectCatalog.DefinitionSql(provider)}) definitions",
                new SugarParameter("@schema", schema) { DbType = System.Data.DbType.String }, new SugarParameter("@name", name), new SugarParameter("@kind", kind),
                new SugarParameter("@characters", limits.Bytes!.Value + 1));
            var text = definition is null or DBNull ? null : Convert.ToString(definition);
            return (object)BoundedQueryExecutor.EncodeResponse(new ObjectDefinitionResult
            {
                Success = true,
                DatabaseName = context.DatabaseName,
                Kind = kind,
                Name = name,
                Schema = schema,
                Definition = text,
                Availability = text == null ? "definition-unavailable" : "available"
            }, limits.Bytes!.Value);
        }, commandTimeoutSeconds, token, databaseName);

    private static void ValidateName(string? name)
    {
        if (name?.Length > 1024 || name?.Contains('\0') == true) throw Invalid("名称超长或包含 NUL。");
    }
    private static DatabaseMcpException Invalid(string message) => new(DatabaseErrorCode.InvalidParameters, message);
}
