using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Schema;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using Microsoft.Extensions.Logging;
using SqlSugar;
using System.Text.Json;

namespace DatabaseMcpServer.Operations;

internal sealed class SchemaReadOperations : SqlOperationExecutor
{
    private readonly DatabaseMetadataCache _metadataCache;
    public SchemaReadOperations(
        IDatabaseConfigService databaseConfig,
        IDatabaseHelperService databaseHelper,
        ILogger<SchemaReadOperations> logger, DatabaseMetadataCache metadataCache, DatabaseExecutionOptions executionOptions, SqlDatabaseSessions sessions)
        : base(databaseConfig, databaseHelper, logger, executionOptions, metadataCache, sessions)
    {
        _metadataCache = metadataCache;
    }

    public object GetDataBaseList(string? databaseName = null) => QueryData(db => db.DbMaintenance.GetDataBaseList(), databaseName, DatabaseCapability.DatabaseListing);

    public object IsAnyTable(string tableName,
        string? databaseName = null)
        => QueryExists(db => db.DbMaintenance.IsAnyTable(tableName, false), databaseName);

    public object IsAnyColumn(
        string tableName,
        string columnName,
        string? databaseName = null)
        => QueryExists(db => db.DbMaintenance.IsAnyColumn(tableName, columnName, false), databaseName);

    public object IsAnyConstraint(string constraintName,
        string? databaseName = null)
        => QueryExists(db => db.DbMaintenance.IsAnyConstraint(constraintName), databaseName, DatabaseCapability.ConstraintLookup);

    public object IsAnyTableRemark(string tableName,
        string? databaseName = null)
        => QueryExists(db => db.CurrentConnectionConfig.DbType == DbType.SqlServer
            ? SqlServerSchemaDialect.HasTableRemark(db, tableName)
            : db.DbMaintenance.IsAnyTableRemark(tableName), databaseName, DatabaseCapability.TableRemarkLookup);

    public Task<object> GetTableSchema(string? tableName, string? databaseName = null,
        bool refresh = false, string[]? sections = null, string[]? tableNames = null,
        int? maxRows = null, int? maxResponseBytes = null, int? commandTimeoutSeconds = null, CancellationToken cancellationToken = default)
    {
        return WithDeadlineAsync(async (context, token) =>
        {
            var requested = (sections?.Select(CapabilityNames<SchemaSectionKind>.Parse) ?? ProviderCapabilities.DefaultSchemaSections)
                .Distinct().OrderBy(CapabilityNames<SchemaSectionKind>.Name, StringComparer.Ordinal).ToArray();
            if (requested.Length == 0)
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "sections 包含未知项或为空。");
            if ((tableName == null) == (tableNames == null))
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "指定 tableName 或 tableNames，不能同时指定。");
            var tables = tableNames ?? [tableName!];
            if (tables.Length is < 1 or > 50 || tables.Distinct(StringComparer.Ordinal).Count() != tables.Length)
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "tableNames 必须是 1-50 个不重复的表名。");
            var provider = context.Client.CurrentConnectionConfig.DbType;
            var product = DatabaseProviderCatalog.Parse(context.DatabaseType);
            ProviderCapabilities.Require(product, DatabaseCapability.Sql);
            var nativeSchema = ProviderCapabilities.Supports(product, DatabaseCapability.AsyncSchema);
            foreach (var table in tables)
            {
                NativeTableMetadata.Parse(table, provider);
                if (!nativeSchema) SqlSafetyGuard.EnsureSafeTableName(table);
            }
            var limits = ExecutionOptions.ResolveQuery(maxRows, maxResponseBytes, commandTimeoutSeconds, bounded: true);
            var remainingRows = limits.Rows!.Value;
            var remainingBytes = limits.Bytes!.Value - 512 - 256 * tables.Length;
            var results = new List<object>();
            JsonElement single = default;
            foreach (var table in tables)
            {
                token.ThrowIfCancellationRequested();
                BoundedQueryExecutor? budget = null;
                var charged = false;
                try
                {
                    if (remainingRows <= 0 || remainingBytes < 256)
                        throw new DatabaseMcpException(DatabaseErrorCode.ResponseLimitExceeded, "共享元数据预算已耗尽。");
                    var data = await _metadataCache.GetAsync(context, table, refresh, async () =>
                    {
                        var document = new Dictionary<string, object?> { ["tableName"] = table };
                        List<NativeTableMetadata.Column>? columns = null;
                        var visible = new NativeSchemaSections.Visibility();
                        budget = new BoundedQueryExecutor(remainingRows, remainingBytes);
                        foreach (var section in requested)
                        {
                            var name = CapabilityNames<SchemaSectionKind>.Name(section);
                            token.ThrowIfCancellationRequested();
                            if (section is SchemaSectionKind.Columns or SchemaSectionKind.PrimaryKeys or SchemaSectionKind.IdentityColumns)
                            {
                                columns ??= nativeSchema
                                    ? await NativeTableMetadata.ColumnsAsync(context.Client, table, budget, token)
                                    : ReadProviderColumns(context.Client, table);
                                token.ThrowIfCancellationRequested();
                                if (columns.Count == 0) throw new DatabaseMcpException(DatabaseErrorCode.QueryExecutionFailed, "表不存在或当前角色不可见。");
                                document[name] = section switch
                                {
                                    SchemaSectionKind.Columns => columns.Select(c => c.Describe()).ToArray(),
                                    SchemaSectionKind.PrimaryKeys => columns.Where(c => c.PrimaryKeyOrdinal > 0).OrderBy(c => c.PrimaryKeyOrdinal).Select(c => c.Name).ToArray(),
                                    _ => columns.Where(c => c.IdentityFlag != 0).Select(c => c.Name).ToArray()
                                };
                            }
                            else if (section == SchemaSectionKind.Indexes)
                                document[name] = nativeSchema
                                    ? await budget.ReadMetadataAsync<Dictionary<string, JsonElement>>(
                                        context.Client, NativeTableMetadata.IndexesSql(provider), NativeTableMetadata.Parameters(table, provider), token)
                                    : context.Client.DbMaintenance.GetIndexList(table);
                            else if (!nativeSchema)
                                document[name] = new { status = "unsupported", reason = "此 provider 未提供该 section。" };
                            else
                            {
                                try { document[name] = await NativeSchemaSections.ReadAsync(context.Client, table, section, budget, visible, token); }
                                catch (Exception ex) when (ex is not OperationCanceledException && !token.IsCancellationRequested &&
                                    ex is not DatabaseMcpException { ErrorCode: DatabaseErrorCode.ResponseLimitExceeded })
                                {
                                    var error = DatabaseErrorMapper.Map(ex, Logger);
                                    document[name] = new { status = "unavailable", reason = "读取失败；检查权限和 provider 支持。", errorCode = error.ErrorCode, error.Retryable, error.Phase };
                                }
                            }
                        }
                        token.ThrowIfCancellationRequested();
                        using var json = JsonDocument.Parse(BoundedQueryExecutor.EncodeResponse(document, remainingBytes).Utf8Json);
                        return json.RootElement.Clone();
                    }, token, string.Join(',', requested));
                    var cost = Math.Max(MetadataRows(data), budget?.RowsRead ?? 0);
                    var bytes = Math.Max(JsonSerializer.SerializeToUtf8Bytes(data).Length, (int)(budget?.BytesConsumed ?? 0));
                    if (cost > remainingRows || bytes > remainingBytes)
                        throw new DatabaseMcpException(DatabaseErrorCode.ResponseLimitExceeded, "元数据超出共享预算。");
                    remainingRows -= cost;
                    remainingBytes -= bytes;
                    charged = true;
                    single = data;
                    results.Add(new { tableName = table, status = "available", data });
                }
                catch (Exception ex) when (tableNames != null && ex is not OperationCanceledException && !token.IsCancellationRequested)
                {
                    var error = DatabaseErrorMapper.Map(ex, Logger);
                    results.Add(new { tableName = table, status = "unavailable", errorCode = error.ErrorCode, error.Retryable, error.Phase });
                    if (error.ErrorCode == DatabaseErrorCode.ResponseLimitExceeded)
                    {
                        remainingRows = 0;
                        remainingBytes = 0;
                        charged = true;
                    }
                }
                finally
                {
                    if (!charged && budget != null)
                    {
                        remainingRows -= budget.RowsRead;
                        remainingBytes -= (int)budget.BytesConsumed;
                    }
                }
            }
            return BoundedQueryExecutor.EncodeResponse(tableNames == null ? (object)new { success = true, data = single }
                : new { success = true, tables = results, rowCount = limits.Rows.Value - remainingRows }, limits.Bytes.Value);
        }, commandTimeoutSeconds, cancellationToken, databaseName);
    }

    private static List<NativeTableMetadata.Column> ReadProviderColumns(ISqlSugarClient client, string table)
    {
        var keyOrdinal = 0;
        return client.DbMaintenance.GetColumnInfosByTableName(table, false).Select((column, index) => new NativeTableMetadata.Column
        {
            Name = column.DbColumnName,
            DataType = column.DataType,
            Ordinal = index + 1,
            PrimaryKeyOrdinal = column.IsPrimarykey ? ++keyOrdinal : 0,
            NullableFlag = column.IsNullable ? 1 : 0,
            IdentityFlag = column.IsIdentity ? 1 : 0,
            DefaultValue = column.DefaultValue
        }).ToList();
    }

    private static int MetadataRows(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Array => value.GetArrayLength() + value.EnumerateArray().Sum(MetadataRows),
        JsonValueKind.Object => value.EnumerateObject().Sum(p => MetadataRows(p.Value)),
        _ => 0
    };

    private object QueryData(Func<ISqlSugarClient, object?> query, string? databaseName, DatabaseCapability capability)
    {
        return WithClientContext(context =>
        {
            ProviderCapabilities.Require(DatabaseProviderCatalog.Parse(context.DatabaseType), capability);
            return new { success = true, data = query(context.Client) };
        }, databaseName);
    }

    private object QueryExists(Func<ISqlSugarClient, bool> query, string? databaseName, DatabaseCapability capability = DatabaseCapability.Sql)
    {
        return WithClientContext(context =>
        {
            ProviderCapabilities.Require(DatabaseProviderCatalog.Parse(context.DatabaseType), capability);
            return new { success = true, exists = query(context.Client) };
        }, databaseName);
    }

}
