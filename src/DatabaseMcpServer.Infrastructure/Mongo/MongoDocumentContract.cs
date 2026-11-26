using System.Text;
using System.Text.Json;
using DatabaseMcpServer.Models;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;

namespace DatabaseMcpServer.Helpers;

internal static class MongoDocumentContract
{
    internal const int MaxInputBytes = 4 * 1024 * 1024;
    internal static readonly JsonWriterSettings Canonical = new() { OutputMode = JsonOutputMode.CanonicalExtendedJson };
    private static readonly HashSet<string> UpdateOperators = new(StringComparer.Ordinal)
    { "$set", "$unset", "$inc", "$mul", "$min", "$max", "$push", "$pull", "$addToSet", "$pop", "$currentDate", "$setOnInsert" };

    public static DatabaseMcpException Invalid(string message) => new(DatabaseErrorCode.InvalidParameters, message);

    public static MongoUrlBuilder ValidateUri(string uri)
    {
        try
        {
            var builder = new MongoUrlBuilder(uri);
            if (uri.StartsWith("mongodb+srv://", StringComparison.OrdinalIgnoreCase))
                throw Invalid("当前 SqlSugar Mongo provider 不支持 mongodb+srv URI；请使用显式主机的 mongodb:// 连接字符串。");
            if (string.IsNullOrWhiteSpace(builder.DatabaseName))
                throw Invalid("Mongo URI 必须显式指定数据库；authSource 不能替代数据库名。");
            return builder;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            throw Invalid("Mongo URI 无效，请检查连接选项。");
        }
    }

    public static void ValidateCollection(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith("system.", StringComparison.Ordinal) || name.Any(char.IsWhiteSpace) || name.Contains('\0') || name.Contains('$'))
            throw Invalid("collection 必须是非空集合名，不能包含空白、NUL、$ 或使用 system. 前缀。");
    }

    // Validate the raw object before dictionary binding, which otherwise loses duplicate top-level keys.
    public static void ValidateObject(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid("Mongo 参数必须是真实 JSON 对象，不能是字符串、数组或 null。");
        if (Encoding.UTF8.GetByteCount(value.GetRawText()) > MaxInputBytes) throw Invalid("Mongo 输入超过 4 MiB。");
        ValidateJson(value, 0);
    }

    private static void ValidateJson(JsonElement value, int depth)
    {
        if (depth > 64) throw Invalid("Mongo JSON 嵌套超过 64 层。");
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name) || property.Name.Contains('\0')) throw Invalid("Mongo JSON 包含重复字段或 NUL 字段名。");
                ValidateJson(property.Value, depth + 1);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) ValidateJson(item, depth + 1);
    }

    public static BsonDocument Parse(Dictionary<string, JsonElement>? value, ref int inputBytes, bool required = false)
    {
        if (value == null)
        {
            if (required) throw Invalid("缺少必填 Mongo JSON 对象。");
            return new();
        }
        try
        {
            var json = JsonSerializer.SerializeToElement(value, JsonSerializationDefaults.CompactCamelCase);
            ValidateObject(json);
            inputBytes += Encoding.UTF8.GetByteCount(json.GetRawText());
            if (inputBytes > MaxInputBytes) throw Invalid("Mongo 对象输入总量超过 4 MiB。");
            var document = BsonDocument.Parse(json.GetRawText());
            ValidateBson(document, false);
            if (document.ToBson().Length > 16 * 1024 * 1024) throw Invalid("BSON 文档超过 16 MiB。");
            return document;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException or InvalidOperationException or OverflowException)
        { throw Invalid("Mongo JSON 或 Extended JSON v2 无效；请检查类型包装及数值范围。"); }
    }

    public static BsonDocument[] ParseMany(Dictionary<string, JsonElement>[]? documents, ref int inputBytes)
    {
        if (documents is not { Length: > 0 and <= 1000 })
            throw Invalid("documents 必须包含 1-1000 个 JSON 对象。");
        var parsed = new List<BsonDocument>(documents.Length);
        foreach (var item in documents)
            parsed.Add(Parse(item, ref inputBytes, required: true));
        return parsed.ToArray();
    }

    public static void ValidateFilter(BsonDocument filter) => ValidateBson(filter, true);

    private static void ValidateBson(BsonValue value, bool filter)
    {
        if (value.BsonType is BsonType.JavaScript or BsonType.JavaScriptWithScope) throw Invalid("不支持 JavaScript BSON 类型。");
        if (value is BsonDocument document)
            foreach (var element in document)
            {
                if (filter && element.Name is "$where" or "$function" or "$accumulator") throw Invalid("过滤器不允许执行 JavaScript。");
                ValidateBson(element.Value, filter);
            }
        else if (value is BsonArray array)
            foreach (var item in array) ValidateBson(item, filter);
    }

    public static void ValidateProjection(BsonDocument projection)
    {
        int? mode = null;
        foreach (var element in projection)
        {
            var value = Direction(element.Value, false);
            if (element.Name == "_id") continue;
            if (mode != null && mode != value) throw Invalid("projection 不能混用包含和排除，_id 除外。");
            mode = value;
        }
    }

    public static void ValidateSort(BsonDocument sort)
    {
        foreach (var element in sort) Direction(element.Value, true);
        if (!sort.Contains("_id")) sort.Add("_id", 1);
    }

    private static int Direction(BsonValue value, bool sort)
    {
        if (!value.IsNumeric || (value != 1 && value != (sort ? -1 : 0)))
            throw Invalid(sort ? "sort 只接受 1/-1。" : "projection 只接受 0/1。");
        return value.ToInt32();
    }

    public static void ValidateUpdate(BsonDocument update)
    {
        if (update.ElementCount == 0) throw Invalid("update 必须包含更新操作符。");
        foreach (var operation in update)
        {
            if (!UpdateOperators.Contains(operation.Name) || operation.Value is not BsonDocument fields || fields.ElementCount == 0)
                throw Invalid("update 必须是受支持的非空操作符对象；不接受 replacement 或 pipeline。");
            foreach (var field in fields)
            {
                if (field.Name == "_id" || field.Name.StartsWith("_id.", StringComparison.Ordinal)) throw Invalid("禁止更新 _id。");
                if (operation.Name == "$pull" && field.Value is BsonDocument predicate) ValidateFilter(predicate);
            }
        }
    }

    public static void EnsureBatchMutationAllowed(BsonDocument filter, bool allowAll, bool dangerous)
    {
        if (!dangerous)
            throw new DatabaseMcpException(DatabaseErrorCode.AccessDenied, "SqlSugar Mongo 批量更新/删除需要 enableDangerousOperations=true。");
        if (filter.ElementCount == 0 && !allowAll)
            throw new DatabaseMcpException(DatabaseErrorCode.AccessDenied, "空 filter 会影响全部文档，必须同时指定 allowAll=true。");
    }

    public static JsonElement ToJson(BsonValue document)
    {
        using var json = JsonDocument.Parse(document.ToJson(Canonical));
        return json.RootElement.Clone();
    }
}
