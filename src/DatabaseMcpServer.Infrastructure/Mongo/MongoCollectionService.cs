using System.Text;
using System.Text.Json;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;
using MongoDB.Bson;

namespace DatabaseMcpServer.Services;

internal sealed class MongoCollectionService(SqlDatabaseSessions sessions, DatabaseExecutionOptions options)
{
    public async Task<MongoCollectionResult> ExecuteAsync(MongoCollectionRequest request, CancellationToken cancellationToken)
    {
        using var measurement = DatabaseTelemetry.MeasureRequest();
        var result = new MongoCollectionResult { WriteOutcome = request.IsWrite ? "not-executed" : null };
        var dispatched = false;
        CancellationToken operationToken = default;
        try
        {
            var limits = options.ResolveQuery(request.MaxRows, request.MaxResponseBytes, request.CommandTimeoutSeconds, bounded: true);
            var maxResponseBytes = limits.Bytes!.Value;
            if (maxResponseBytes < 1024) throw MongoDocumentContract.Invalid("Mongo 响应至少需要 1024 字节预算。");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var cancellation = DatabaseTelemetry.MeasureCancellation(deadline.Token);
            var timeout = limits.TimeoutSeconds > 0 ? TimeSpan.FromSeconds(limits.TimeoutSeconds.Value) : Timeout.InfiniteTimeSpan;
            if (timeout != Timeout.InfiniteTimeSpan) deadline.CancelAfter(timeout);
            var token = deadline.Token;
            operationToken = token;

            using var lease = await (request.DatabaseName == null
                ? sessions.AcquireClientAsync(token)
                : sessions.AcquireClientAsync(request.DatabaseName, token)).ConfigureAwait(false);
            var context = lease.Context;
            var requiredAccess = request.Operation switch
            {
                MongoOperation.Truncate => DatabaseToolAccess.Schema,
                _ when request.IsWrite => DatabaseToolAccess.Write,
                _ => DatabaseToolAccess.Read
            };
            DatabaseAccessPolicy.EnsureAllowed(context, requiredAccess, options.Access);
            if (DatabaseProviderCatalog.Parse(context.DatabaseType) != SqlSugar.DbType.MongoDb)
                throw new DatabaseMcpException(DatabaseErrorCode.UnsupportedCapability, "Mongo 集合工具只支持 MongoDb 连接。");

            result.DatabaseName = context.DatabaseName;
            result.ConfigurationVersion = context.ConfigurationVersion;

            MongoDocumentContract.ValidateCollection(request.Collection);
            if (request.Skip is < 0 or > 100000) throw MongoDocumentContract.Invalid("skip 必须在 0–100000 之间。");

            var inputBytes = 0;
            var filter = MongoDocumentContract.Parse(request.Filter, ref inputBytes, request.Operation is MongoOperation.Update or MongoOperation.Delete);
            var projection = MongoDocumentContract.Parse(request.Projection, ref inputBytes);
            var sort = MongoDocumentContract.Parse(request.Sort, ref inputBytes);
            var update = MongoDocumentContract.Parse(request.Update, ref inputBytes, request.Operation == MongoOperation.Update);
            var documents = request.Operation == MongoOperation.InsertMany ? MongoDocumentContract.ParseMany(request.Documents, ref inputBytes) : null;

            MongoDocumentContract.ValidateFilter(filter);
            MongoDocumentContract.ValidateProjection(projection);
            MongoDocumentContract.ValidateSort(sort);
            BsonDocument[]? pipeline = request.Operation == MongoOperation.Aggregate ? MongoPipelineContract.Parse(request.Pipeline, ref inputBytes) : null;

            if (request.Operation == MongoOperation.Update) MongoDocumentContract.ValidateUpdate(update);
            if (request.Operation is MongoOperation.Update or MongoOperation.Delete)
                MongoDocumentContract.EnsureBatchMutationAllowed(filter, request.AllowAll, context.EnableDangerousOperations);
            if (request.Operation == MongoOperation.Truncate && !context.EnableDangerousOperations)
                throw new DatabaseMcpException(DatabaseErrorCode.AccessDenied, "mongo_truncate 需要 enableDangerousOperations=true。");

            var uri = MongoDocumentContract.ValidateUri(context.ConnectionString);
            if (Encoding.UTF8.GetByteCount(uri.DatabaseName + "." + request.Collection) > 255)
                throw MongoDocumentContract.Invalid("数据库名与集合名组成的 namespace 超过 255 UTF-8 字节。");
            if (request.IsWrite && new MongoDB.Driver.WriteConcern(uri.W).IsAcknowledged == false)
                throw MongoDocumentContract.Invalid("集合写入不支持 w=0；请配置 acknowledged write concern。");

            EnsureFits(result, maxResponseBytes, 128);
            var driverTimeout = limits.TimeoutSeconds is > 0 ? limits.TimeoutSeconds + 1 : limits.TimeoutSeconds;

            await SqlCommandTimeout.WithTimeoutAsync(lease.Client, driverTimeout, token, async db =>
            {
                switch (request.Operation)
                {
                    case MongoOperation.Find:
                    {
                        var stages = BuildFindPipeline(filter, sort, request.Skip, limits.Rows!.Value, projection);
                        await ReadAggregateAsync(db, request.Collection!, stages, result, limits.Rows.Value, maxResponseBytes, token).ConfigureAwait(false);
                        break;
                    }
                    case MongoOperation.Aggregate:
                        await ReadAggregateAsync(db, request.Collection!, pipeline!, result, limits.Rows!.Value, maxResponseBytes, token).ConfigureAwait(false);
                        break;
                    case MongoOperation.InsertMany:
                    {
                        dispatched = true;
                        var json = new BsonArray(documents!).ToJson(MongoDocumentContract.Canonical);
                        result.InsertedCount = await db.Ado.ExecuteCommandAsync($"insertMany {request.Collection} {json}").ConfigureAwait(false);
                        break;
                    }
                    case MongoOperation.Update:
                    {
                        dispatched = true;
                        var payload = new BsonArray([new BsonDocument { { "filter", filter }, { "update", update } }]).ToJson(MongoDocumentContract.Canonical);
                        result.ModifiedCount = await db.Ado.ExecuteCommandAsync($"updateMany {request.Collection} {payload}").ConfigureAwait(false);
                        break;
                    }
                    case MongoOperation.Delete:
                    {
                        dispatched = true;
                        var payload = new BsonArray([new BsonDocument("filter", filter)]).ToJson(MongoDocumentContract.Canonical);
                        result.DeletedCount = await db.Ado.ExecuteCommandAsync($"deleteMany {request.Collection} {payload}").ConfigureAwait(false);
                        break;
                    }
                    case MongoOperation.Truncate:
                        dispatched = true;
                        await db.Ado.ExecuteCommandAsync($"deleteMany {request.Collection} [{{\"filter\":{{}}}}]").ConfigureAwait(false);
                        break;
                }
                return true;
            }).ConfigureAwait(false);

            if (request.IsWrite)
            {
                result.Acknowledged = true;
                result.WriteOutcome = "acknowledged";
            }
            EnsureFits(result, maxResponseBytes);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            DatabaseTelemetry.Cancelled.Add(1);
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (Exception ex)
        {
            if (operationToken.IsCancellationRequested)
            {
                DatabaseTelemetry.Cancelled.Add(1);
                DatabaseTelemetry.RequestTimeouts.Add(1);
            }
            var error = MongoError.Sanitize(ex, operationToken.IsCancellationRequested);
            var details = ApiResult<object>.CreateError(error.Message, error.ErrorCode);
            var acknowledged = result.WriteOutcome == "acknowledged";
            string? writeOutcome = null;
            if (request.IsWrite)
            {
                if (acknowledged) writeOutcome = "acknowledged";
                else writeOutcome = dispatched ? "unknown" : "not-executed";
            }
            result = new MongoCollectionResult
            {
                Success = false, ErrorCode = (int)error.ErrorCode, ErrorMessage = error.Message,
                Retryable = details.Retryable, Phase = error.Phase ?? details.Phase, Capability = error.Capability, Hint = details.Hint,
                WriteOutcome = writeOutcome,
                Acknowledged = acknowledged ? true : null
            };
        }
        return result;
    }

    private static BsonDocument[] BuildFindPipeline(BsonDocument filter, BsonDocument sort, int skip, int limit, BsonDocument projection)
    {
        var stages = new List<BsonDocument>();
        if (filter.ElementCount > 0) stages.Add(new BsonDocument("$match", filter));
        stages.Add(new BsonDocument("$sort", sort));
        if (skip > 0) stages.Add(new BsonDocument("$skip", skip));
        stages.Add(new BsonDocument("$limit", limit));
        if (projection.ElementCount > 0) stages.Add(new BsonDocument("$project", projection));
        return [.. stages];
    }

    private static async Task ReadAggregateAsync(SqlSugar.ISqlSugarClient db, string collection, IEnumerable<BsonDocument> pipeline,
        MongoCollectionResult result, int maxRows, int maxBytes, CancellationToken token)
    {
        using var measurement = DatabaseTelemetry.MeasureRead();
        var stages = pipeline.Append(new BsonDocument("$limit", maxRows));
        var command = $"aggregate {collection} {new BsonArray(stages).ToJson(MongoDocumentContract.Canonical)}";
        using var reader = await db.Ado.GetDataReaderAsync(command).ConfigureAwait(false);
        result.Documents = [];
        result.ReturnedCount = maxRows;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(result, JsonSerializationDefaults.CompactCamelCase).Length;
        result.ReturnedCount = 0;

        while (await QueryReader.ReadAsync(reader, token).ConfigureAwait(false))
        {
            var fields = new Dictionary<string, object?>();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                fields.Add(reader.GetName(i), reader.GetValue(i) switch
                {
                    DBNull => null,
                    BsonValue bson => MongoDocumentContract.ToJson(bson),
                    var field => field
                });
            }
            var value = JsonSerializer.SerializeToElement(fields, JsonSerializationDefaults.CompactCamelCase);
            var added = JsonSerializer.SerializeToUtf8Bytes(value, JsonSerializationDefaults.CompactCamelCase).Length + (result.Documents.Count > 0 ? 1 : 0);
            if (bytes + added > maxBytes)
            {
                result.LimitReached = true;
                result.Truncated = true;
                return;
            }
            result.Documents.Add(value);
            bytes += added;
            result.ReturnedCount = result.Documents.Count;
            if (result.Documents.Count == maxRows)
            {
                result.LimitReached = true;
                result.Truncated = null;
                return;
            }
        }
    }

    private static void EnsureFits(MongoCollectionResult result, int bytes, int reserve = 0)
    {
        if (JsonSerializer.SerializeToUtf8Bytes(result, JsonSerializationDefaults.CompactCamelCase).Length + reserve > bytes)
            throw new DatabaseMcpException(DatabaseErrorCode.ResponseLimitExceeded, "Mongo 响应元数据超出字节预算，请增加预算。");
    }
}
