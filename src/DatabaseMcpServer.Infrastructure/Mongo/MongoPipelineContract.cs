using System.Text.Json;
using MongoDB.Bson;

namespace DatabaseMcpServer.Helpers;

internal static class MongoPipelineContract
{
    internal static readonly string[] SupportedStages = ["$match", "$project", "$group", "$sort", "$limit", "$skip", "$unwind", "$count", "$addFields", "$set", "$unset", "$replaceRoot", "$replaceWith", "$sortByCount", "$lookup", "$facet"];

    public static BsonDocument[] Parse(Dictionary<string, JsonElement>[]? pipeline, ref int inputBytes)
    {
        if (pipeline is not { Length: > 0 and <= 64 }) throw MongoDocumentContract.Invalid("pipeline 必须包含 1-64 个 stage。");
        var stages = new List<BsonDocument>(pipeline.Length);
        var count = 0;
        foreach (var input in pipeline)
        {
            var stage = MongoDocumentContract.Parse(input, ref inputBytes, required: true);
            ValidateStage(stage, 0, false, ref count);
            stages.Add(stage);
        }
        return stages.ToArray();
    }

    private static void ValidateStage(BsonDocument stage, int depth, bool insideFacet, ref int count)
    {
        if (++count > 64 || depth > 4) throw MongoDocumentContract.Invalid("嵌套 pipeline 总共最多 64 个 stage、4 层。");
        if (stage.ElementCount != 1 || !SupportedStages.Contains(stage.GetElement(0).Name, StringComparer.Ordinal))
            throw MongoDocumentContract.Invalid("pipeline stage 不在只读允许列表内。");
        ValidateTree(stage);
        var element = stage.GetElement(0);
        if (element.Name == "$lookup")
        {
            if (element.Value is not BsonDocument lookup || !lookup.TryGetValue("from", out var from) || !from.IsString)
                throw MongoDocumentContract.Invalid("$lookup.from 必须是当前数据库的集合名，不能指定跨库对象。");
            MongoDocumentContract.ValidateCollection(from.AsString);
            if (!lookup.TryGetValue("as", out var target) || !target.IsString || string.IsNullOrWhiteSpace(target.AsString) || target.AsString.StartsWith('$'))
                throw MongoDocumentContract.Invalid("$lookup.as 必须是输出字段名。");
            if (lookup.Names.Any(n => n is not ("from" or "as" or "localField" or "foreignField" or "let" or "pipeline")))
                throw MongoDocumentContract.Invalid("$lookup 包含未知选项。");
            if (lookup.TryGetValue("pipeline", out var nested)) ValidateNested(nested, depth + 1, insideFacet, ref count);
            else if (!lookup.TryGetValue("localField", out var local) || !local.IsString || !lookup.TryGetValue("foreignField", out var foreign) || !foreign.IsString)
                throw MongoDocumentContract.Invalid("$lookup 需要 localField/foreignField 或 pipeline。");
        }
        else if (element.Name == "$facet")
        {
            if (insideFacet || element.Value is not BsonDocument facets || facets.ElementCount is < 1 or > 8)
                throw MongoDocumentContract.Invalid("$facet 允许 1-8 个分支，不允许在 facet 内再次嵌套 facet。");
            foreach (var facet in facets)
            {
                if (string.IsNullOrWhiteSpace(facet.Name) || facet.Name.StartsWith('$') || facet.Name.Contains('.'))
                    throw MongoDocumentContract.Invalid("$facet 分支名称无效。");
                ValidateNested(facet.Value, depth + 1, true, ref count);
            }
        }
    }

    private static void ValidateNested(BsonValue value, int depth, bool insideFacet, ref int count)
    {
        if (value is not BsonArray array) throw MongoDocumentContract.Invalid("嵌套 pipeline 必须是 stage 数组。");
        foreach (var child in array)
        {
            if (child is not BsonDocument document) throw MongoDocumentContract.Invalid("pipeline stage 必须是对象。");
            ValidateStage(document, depth, insideFacet, ref count);
        }
    }

    private static void ValidateTree(BsonValue value)
    {
        if (value is BsonDocument document)
            foreach (var element in document)
            {
                if (element.Name is "$out" or "$merge" or "$where" or "$function" or "$accumulator")
                    throw MongoDocumentContract.Invalid("pipeline 的嵌套表达式禁止写 stage 和 JavaScript。");
                ValidateTree(element.Value);
            }
        else if (value is BsonArray array)
            foreach (var item in array) ValidateTree(item);
    }
}
