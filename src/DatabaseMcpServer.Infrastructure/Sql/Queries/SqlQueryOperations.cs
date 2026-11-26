using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using Microsoft.Extensions.Logging;
using SqlSugar;
using System.Data;
using System.Text.Json;

namespace DatabaseMcpServer.Operations;

/// <summary>
/// 数据库查询工具类，支持各种查询操作。
/// </summary>
internal class SqlQueryOperations : SqlOperationExecutor
{
    private const int MaxBatchQueryCount = 5;

    public Task<object> QueryTablePage(
        string tableName,
        int page = 1,
        int pageSize = 100,
        string? databaseName = null,
        int? maxResponseBytes = null,
        int? commandTimeoutSeconds = null,
        CancellationToken cancellationToken = default, string? cursor = null,
        string[]? columns = null, TableFilter[]? filters = null, TableOrder[]? orderBy = null)
        => WithDeadlineAsync(async (context, token) =>
        {
            if (page < 1 || (long)page * pageSize > int.MaxValue)
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "page 超出有效范围。");
            var limits = ExecutionOptions.ResolveQuery(pageSize, maxResponseBytes, commandTimeoutSeconds, bounded: true);
            SqlSafetyGuard.EnsureSafeTableName(tableName);
            var db = context.Client;
            var provider = db.CurrentConnectionConfig.DbType;
            ProviderCapabilities.Require(DatabaseProviderCatalog.Parse(context.DatabaseType), DatabaseCapability.BoundedResults);
            if (cursor != null && page != 1)
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "cursor 模式不能同时指定 page > 1。");
            var hasOptions = columns != null || filters != null || orderBy != null;
            if (hasOptions && cursor != null)
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "筛选、选列或排序使用 page 分页，不能同时指定 cursor。");
            if (columns is { Length: 0 or > 64 } || filters is { Length: > 32 } || orderBy is { Length: > 8 })
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "columns 必须为 1-64 项，filters 最多 32 项，orderBy 最多 8 项。");
            var client = db;
            var tableColumns = await NativeTableMetadata.ColumnsAsync(client, tableName,
                new BoundedQueryExecutor(Math.Min(4096, ExecutionOptions.MaxQueryRows), ExecutionOptions.MaxResponseBytes), token);
            var primaryColumns = tableColumns.Where(c => c.PrimaryKeyOrdinal > 0).OrderBy(c => c.PrimaryKeyOrdinal).ToArray();
            var primary = primaryColumns.Select(c => c.Name).ToArray();
            if (primary.Length == 0)
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "分页需要主键保证唯一排序；请为无主键表使用明确排序的查询。");
            if (primaryColumns.Any(c => c.NullableFlag != 0))
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "分页需要非空主键保证唯一排序；请为可空主键表使用明确排序的查询。");
            foreach (var key in primary) SqlSafetyGuard.EnsureSafeTableName(key);
            object data;
            int rowCount;
            var effectiveOrder = new List<TableOrder>();
            string? next = null;
            if (hasOptions)
            {
                var names = tableColumns.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
                var requestedNames = (columns ?? []).Concat((filters ?? []).Select(f => f?.Column))
                    .Concat((orderBy ?? []).Select(o => o?.Column));
                foreach (var name in requestedNames)
                {
                    if (name == null || name.Contains('.') || !names.Contains(name))
                        throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "选列、筛选和排序必须使用表中存在的普通字段名（区分大小写）。");
                    SqlSafetyGuard.EnsureSafeTableName(name);
                }
                if (columns != null && columns.Distinct(StringComparer.Ordinal).Count() != columns.Length)
                    throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "columns 不能重复。");

                var conditions = new List<IConditionalModel>();
                foreach (var filter in filters ?? [])
                {
                    var condition = new ConditionalModel { FieldName = filter.Column };
                    condition.ConditionalType = filter.Op switch
                    {
                        TableFilterOperator.Eq => ConditionalType.Equal,
                        TableFilterOperator.Ne => ConditionalType.NoEqual,
                        TableFilterOperator.Gt => ConditionalType.GreaterThan,
                        TableFilterOperator.Gte => ConditionalType.GreaterThanOrEqual,
                        TableFilterOperator.Lt => ConditionalType.LessThan,
                        TableFilterOperator.Lte => ConditionalType.LessThanOrEqual,
                        TableFilterOperator.IsNull => ConditionalType.EqualNull,
                        TableFilterOperator.IsNotNull => ConditionalType.IsNot,
                        _ => throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "不支持的筛选 op。")
                    };
                    if (filter.Op is TableFilterOperator.IsNull or TableFilterOperator.IsNotNull)
                    {
                        if (filter.Value.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null) || filter.DbType != null)
                            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "空值判断不接受 value 或 dbType。");
                    }
                    else
                    {
                        if (filter.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False))
                            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "比较条件需要非空标量 value；空值请使用 isNull/isNotNull。");
                        var value = SqlParameterBinder.Bind([new DatabaseParameter
                        {
                            Name = "filter", Value = filter.Value, DbType = filter.DbType
                        }], provider)[0].Value;
                        // Equal treats the text "null" specially; always supply the typed value through its converter.
                        condition.FieldValue = "value";
                        condition.FieldValueConvertFunc = _ => value;
                    }
                    conditions.Add(condition);
                }
                foreach (var item in orderBy ?? [])
                {
                    if (!Enum.IsDefined(item.Direction) || effectiveOrder.Any(o => o.Column == item.Column))
                        throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "排序方向必须为 asc/desc，排序字段不能重复。");
                    effectiveOrder.Add(item);
                }
                effectiveOrder.AddRange(primary.Where(key => effectiveOrder.All(o => o.Column != key))
                    .Select(key => new TableOrder { Column = key }));
                var sort = effectiveOrder.Select(o => new OrderByModel
                {
                    FieldName = o.Column, OrderByType = o.Direction == TableSortDirection.Desc ? OrderByType.Desc : OrderByType.Asc
                }).ToList();
                var selected = columns ?? tableColumns.Select(c => c.Name).ToArray();
                using var table = await db.Queryable<object>().AS(tableName).Where(conditions).OrderBy(sort)
                    .Select(selected.Select(name => new SelectModel { FieldName = name, AsName = name }).ToList())
                    .ToOffsetDataTablePageAsync(page, pageSize);
                data = table.Rows.Cast<DataRow>().Select(row => table.Columns.Cast<DataColumn>()
                    .ToDictionary(column => column.ColumnName, column => row.IsNull(column) ? null : row[column])).ToArray();
                rowCount = table.Rows.Count;
            }
            else
            {
                var quoted = primary.Select(key => SqlSafetyGuard.QuoteTableName(key, provider)).ToArray();
                var order = string.Join(",", quoted.Select(key => key + " asc"));
                var binding = KeysetCursor.Bind(context, new { tableName, primary, pageSize });
                var keys = KeysetCursor.Decode(cursor, binding, primary.Length);
                var parameters = SqlParameterBinder.Bind(keys, provider).ToList();
                parameters.Add(new("@take", pageSize));
                parameters.Add(new("@offset", (page - 1) * pageSize));
                var sql = $"SELECT * FROM {SqlSafetyGuard.QuoteTableName(tableName, provider)}" +
                    (keys == null ? "" : " WHERE " + KeysetCursor.Predicate(quoted)) + " ORDER BY " + order +
                    (provider == SqlSugar.DbType.SqlServer ? " OFFSET @offset ROWS FETCH NEXT @take ROWS ONLY" : " LIMIT @take OFFSET @offset");
                await using var readerLease = await SqlSugarReaderAdapter.OpenAsync(client, sql, parameters.ToArray(), token);
                var reader = readerLease.Reader;
                var types = primary.Select(key => reader.GetFieldType(reader.GetOrdinal(key))).ToArray();
                var result = await new BoundedQueryExecutor(pageSize, limits.Bytes, 1024).ReadResultAsync(context, reader, token);
                if (result.Truncated == true)
                    throw new DatabaseMcpException(DatabaseErrorCode.ResponseLimitExceeded, "页内容超出预算，请缩小 pageSize 或增加 maxResponseBytes。");
                if (!result.LimitReached) readerLease.Complete();
                data = result.Data;
                rowCount = result.Data.Count;
                using var lastRow = rowCount == pageSize ? JsonDocument.Parse(result.Data[^1].Utf8Json) : null;
                next = lastRow != null ? KeysetCursor.Encode(binding,
                    primary.Select((key, i) => KeysetCursor.Key(i, lastRow.RootElement.GetProperty(key), types[i])).ToArray()) : null;
            }
            return (object)BoundedQueryExecutor.EncodeResponse(new
            {
                success = true,
                databaseName = context.DatabaseName,
                configurationVersion = context.ConfigurationVersion,
                page,
                pageSize,
                rowCount,
                data,
                orderBy = hasOptions ? effectiveOrder.Select(o => o.Column).ToArray() : primary,
                effectiveOrderBy = hasOptions ? effectiveOrder : null,
                nextCursor = next,
                pagingMode = hasOptions || cursor == null && page > 1 ? "offset" : "keyset",
                mayHaveMore = rowCount == pageSize,
                consistency = "provider-isolation; no cross-page snapshot"
            }, limits.Bytes!.Value);
        }, commandTimeoutSeconds, cancellationToken, databaseName);

    public SqlQueryOperations(
        IDatabaseConfigService databaseConfig,
        IDatabaseHelperService databaseHelper,
        ILogger<SqlQueryOperations> logger, DatabaseExecutionOptions executionOptions, DatabaseMetadataCache metadataCache, SqlDatabaseSessions sessions)
        : base(databaseConfig, databaseHelper, logger, executionOptions, metadataCache, sessions)
    {
    }

    public Task<object> GetDataSetAll(
        string sql,
        DatabaseParameter[]? parameters = null,
        int? commandTimeoutSeconds = null,
        int? maxRows = null,
        int? maxResponseBytes = null,
        CancellationToken cancellationToken = default,
        string? databaseName = null)
    {
        return WithDeadlineAsync(async (context, token) =>
        {
            var product = DatabaseProviderCatalog.Parse(context.DatabaseType);
            ProviderCapabilities.Require(product, DatabaseCapability.BoundedResults);
            var db = context.Client;
            EnsureSafeSql(sql, allowMultipleStatements: true);
            var limits = ExecutionOptions.ResolveQuery(maxRows, maxResponseBytes, commandTimeoutSeconds);
            var parsedParams = SqlParameterBinder.Bind(parameters, db.CurrentConnectionConfig.DbType);
            return await new BoundedQueryExecutor(limits.Rows, limits.Bytes).MultipleAsync(db, sql, parsedParams, token, product);
        }, commandTimeoutSeconds, cancellationToken, databaseName);
    }

    public Task<object> BatchSqlQuery(
        DatabaseCommand[] commands,
        int? commandTimeoutSeconds = null,
        int? maxRows = null,
        int? maxResponseBytes = null,
        CancellationToken cancellationToken = default,
        string? databaseName = null)
    {
        return WithDeadlineAsync(async (context, token) =>
        {
            var limits = ExecutionOptions.ResolveQuery(maxRows, maxResponseBytes, commandTimeoutSeconds);
            var queryList = ParseBatchQueries(commands);
            var budget = new BoundedQueryExecutor(limits.Rows, limits.Bytes, 256 + 768 * queryList.Length);
            return await budget.BatchAsync(context.Client, queryList, query => EnsureSafeSql(query), token);
        }, commandTimeoutSeconds, cancellationToken, databaseName);
    }

    private static DatabaseCommand[] ParseBatchQueries(DatabaseCommand[] commands)
    {
        var queryList = commands ?? throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "commands 不能为空。");
        if (queryList.Any(command => command == null || string.IsNullOrWhiteSpace(command.Sql)))
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "每项必须提供非空 SQL。");

        if (queryList.Length == 0)
        {
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "commands 数组不能为空");
        }

        if (queryList.Length > MaxBatchQueryCount)
        {
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, $"一次最多允许执行 {MaxBatchQueryCount} 条查询");
        }

        return queryList;
    }

    private void EnsureSafeSql(string sql, bool allowMultipleStatements = false)
    {
        SqlSafetyGuard.EnsureReadOnlySql(sql, DatabaseHelper, allowMultipleStatements);
    }

}
