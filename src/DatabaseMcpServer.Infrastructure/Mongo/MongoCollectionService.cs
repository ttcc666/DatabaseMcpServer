using System.Text.Json;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DatabaseMcpServer.Services;

internal sealed class MongoCollectionService(IDatabaseConfigService databases, MongoClientRegistry clients, DatabaseExecutionOptions options)
{
    public async Task<MongoCollectionResult> ExecuteAsync(MongoCollectionRequest request, CancellationToken cancellationToken)
    {
        using var measurement = DatabaseTelemetry.MeasureRequest();
        var result = new MongoCollectionResult { WriteOutcome = request.IsWrite ? "not-executed" : null };
        var maximum = Math.Min(options.DefaultResponseBytes, options.MaxResponseBytes);
        var dispatched = false;
        CancellationToken operationToken = default;
        try
        {
            var limits = options.ResolveQuery(request.MaxRows, request.MaxResponseBytes, request.CommandTimeoutSeconds, bounded: true);
            maximum = limits.Bytes!.Value;
            if (maximum < 1024) throw MongoDocumentContract.Invalid("Mongo 响应至少需要 1024 字节预算。");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var timeout = limits.TimeoutSeconds > 0 ? TimeSpan.FromSeconds(limits.TimeoutSeconds.Value) : Timeout.InfiniteTimeSpan;
            if (timeout != Timeout.InfiniteTimeSpan) deadline.CancelAfter(timeout);
            var token = deadline.Token;
            operationToken = token;
            using var reservation = databases.ReserveConnection(request.DatabaseName);
            using var lease = await reservation.AcquireAsync(token);
            var context = lease.Context;
            DatabaseAccessPolicy.EnsureAllowed(context, request.IsWrite ? DatabaseToolAccess.Write : DatabaseToolAccess.Read, options.Access);
            if (DatabaseProviderCatalog.Parse(context.DatabaseType) != SqlSugar.DbType.MongoDb)
                throw new DatabaseMcpException(DatabaseErrorCode.UnsupportedCapability, "Mongo 集合工具只支持 MongoDb 连接。");
            result.DatabaseName = context.DatabaseName;
            result.ConfigurationVersion = context.ConfigurationVersion;
            if (request.Operation != MongoOperation.List) MongoDocumentContract.ValidateCollection(request.Collection);
            if (request.Skip is < 0 or > 100000) throw MongoDocumentContract.Invalid("skip 必须在 0–100000 之间。");
            var inputBytes = request.NamePrefix == null ? 0 : System.Text.Encoding.UTF8.GetByteCount(request.NamePrefix);
            if (inputBytes > MongoDocumentContract.MaxInputBytes) throw MongoDocumentContract.Invalid("Mongo 输入超过 4 MiB。");
            var filter = MongoDocumentContract.Parse(request.Filter, ref inputBytes, request.Operation is MongoOperation.Update or MongoOperation.Delete);
            var projection = MongoDocumentContract.Parse(request.Projection, ref inputBytes);
            var sort = MongoDocumentContract.Parse(request.Sort, ref inputBytes);
            var document = MongoDocumentContract.Parse(request.Document, ref inputBytes, request.Operation == MongoOperation.Insert);
            var update = MongoDocumentContract.Parse(request.Update, ref inputBytes, request.Operation == MongoOperation.Update);
            MongoDocumentContract.ValidateFilter(filter);
            MongoDocumentContract.ValidateProjection(projection);
            MongoDocumentContract.ValidateSort(sort);
            if (request.Operation == MongoOperation.Update) MongoDocumentContract.ValidateUpdate(update);
            if (request.Operation is MongoOperation.Update or MongoOperation.Delete)
                MongoDocumentContract.EnsureMutationAllowed(filter, request.Many, request.Upsert, request.AllowAll, context.EnableDangerousOperations);
            var uri = MongoClientRegistry.ValidateUri(context.ConnectionString);
            if (request.Collection != null && System.Text.Encoding.UTF8.GetByteCount(uri.DatabaseName + "." + request.Collection) > 255)
                throw MongoDocumentContract.Invalid("数据库名与集合名组成的 namespace 超过 255 UTF-8 字节。");
            if (request.IsWrite && new WriteConcern(uri.W).IsAcknowledged == false)
                throw MongoDocumentContract.Invalid("集合写入不支持 w=0；请配置 acknowledged write concern。");
            if (request.Operation == MongoOperation.Insert)
            {
                if (!document.Contains("_id")) document.InsertAt(0, new BsonElement("_id", ObjectId.GenerateNewId()));
                result.InsertedId = MongoDocumentContract.IdToJson(document["_id"]);
            }
            EnsureFits(result, maximum, 128);
            using var mongo = await clients.AcquireAsync(context, token);
            var collection = request.Collection == null ? null : mongo.Database.GetCollection<BsonDocument>(request.Collection);
            if (request.IsWrite && !collection!.Settings.WriteConcern.IsAcknowledged)
                throw MongoDocumentContract.Invalid("集合写入不支持 unacknowledged write concern。");
            token.ThrowIfCancellationRequested();
            switch (request.Operation)
            {
                case MongoOperation.List:
                    BsonDocument? names = request.NamePrefix == null ? null : new("name", new BsonRegularExpression("^" + System.Text.RegularExpressions.Regex.Escape(request.NamePrefix)));
                    using (var cursor = await mongo.Database.ListCollectionsAsync(new ListCollectionsOptions
                    { Filter = names, BatchSize = Math.Min(64, limits.Rows!.Value) }, token))
                    {
                        result.Collections = [];
                        await ReadCursorAsync(cursor, result, result.Collections, limits.Rows!.Value, maximum, token,
                            value => new BsonDocument { { "name", value["name"] }, { "type", value.GetValue("type", "collection") } });
                    }
                    break;
                case MongoOperation.Find:
                    using (var cursor = await collection!.FindAsync(filter, new FindOptions<BsonDocument>
                    { Projection = projection.ElementCount == 0 ? null : projection, Sort = sort, Skip = request.Skip,
                        Limit = limits.Rows, BatchSize = Math.Min(64, limits.Rows!.Value),
                        MaxTime = timeout == Timeout.InfiniteTimeSpan ? null : timeout }, token))
                    {
                        result.Documents = [];
                        await ReadCursorAsync(cursor, result, result.Documents, limits.Rows!.Value, maximum, token);
                    }
                    break;
                case MongoOperation.Count:
                    result.MatchedCount = await collection!.CountDocumentsAsync(filter, new CountOptions
                    { MaxTime = timeout == Timeout.InfiniteTimeSpan ? null : timeout }, token);
                    break;
                case MongoOperation.Insert:
                    dispatched = true;
                    await collection!.InsertOneAsync(document, cancellationToken: token);
                    break;
                case MongoOperation.Update:
                    dispatched = true;
                    var updateOptions = new UpdateOptions { IsUpsert = request.Upsert };
                    var updated = request.Many ? await collection!.UpdateManyAsync(filter, update, updateOptions, token)
                        : await collection!.UpdateOneAsync(filter, update, updateOptions, token);
                    result.MatchedCount = updated.MatchedCount;
                    result.ModifiedCount = updated.ModifiedCount;
                    if (updated.UpsertedId != null) result.UpsertedId = MongoDocumentContract.IdToJson(updated.UpsertedId);
                    break;
                case MongoOperation.Delete:
                    dispatched = true;
                    var deleteOptions = new DeleteOptions();
                    var deleted = request.Many ? await collection!.DeleteManyAsync(filter, deleteOptions, token)
                        : await collection!.DeleteOneAsync(filter, deleteOptions, token);
                    result.DeletedCount = deleted.DeletedCount;
                    break;
            }
            if (request.IsWrite) { result.Acknowledged = true; result.WriteOutcome = "acknowledged"; }
            EnsureFits(result, maximum);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        { DatabaseTelemetry.Cancelled.Add(1); cancellationToken.ThrowIfCancellationRequested(); throw; }
        catch (Exception ex)
        {
            var error = MongoError.Sanitize(ex, operationToken.IsCancellationRequested);
            result = new MongoCollectionResult { Success = false, ErrorCode = (int)error.ErrorCode, ErrorMessage = error.Message,
                WriteOutcome = request.IsWrite ? result.WriteOutcome == "acknowledged" ? "acknowledged" : dispatched ? "unknown" : "not-executed" : null,
                Acknowledged = result.WriteOutcome == "acknowledged" ? true : null };
        }
        return result;
    }

    private static async Task ReadCursorAsync(IAsyncCursor<BsonDocument> cursor, MongoCollectionResult result,
        List<JsonElement> values, int maxRows, int maxBytes, CancellationToken token, Func<BsonDocument, BsonDocument>? select = null)
    {
        // Reserve the largest possible row count and state. Each document is encoded exactly once.
        result.ReturnedCount = maxRows;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(result, JsonSerializationDefaults.CompactCamelCase).Length;
        result.ReturnedCount = 0;
        while (await cursor.MoveNextAsync(token))
        {
            foreach (var document in cursor.Current)
            {
                token.ThrowIfCancellationRequested();
                var value = MongoDocumentContract.ToJson(select == null ? document : select(document));
                var added = JsonSerializer.SerializeToUtf8Bytes(value, JsonSerializationDefaults.CompactCamelCase).Length + (values.Count > 0 ? 1 : 0);
                if (bytes + added > maxBytes) { result.LimitReached = true; result.Truncated = true; return; }
                values.Add(value);
                bytes += added;
                result.ReturnedCount = values.Count;
                if (values.Count == maxRows) { result.LimitReached = true; result.Truncated = null; return; }
            }
        }
    }

    private static void EnsureFits(MongoCollectionResult result, int bytes, int reserve = 0)
    {
        if (JsonSerializer.SerializeToUtf8Bytes(result, JsonSerializationDefaults.CompactCamelCase).Length + reserve > bytes)
            throw new DatabaseMcpException(DatabaseErrorCode.ResponseLimitExceeded, "Mongo 响应元数据或 ID 超出字节预算，请增加预算。");
    }

}
