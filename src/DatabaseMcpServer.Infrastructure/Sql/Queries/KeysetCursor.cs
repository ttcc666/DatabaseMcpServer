using System.Security.Cryptography;
using System.Text.Json;
using DatabaseMcpServer.Models;
using SqlSugar;

namespace DatabaseMcpServer.Helpers;

internal static class KeysetCursor
{
    private sealed record Payload(string Binding, DatabaseParameter[] Keys);
    public static string Bind(DatabaseOperationContext context, object query)
        => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            Version = 2,
            context.DatabaseName,
            context.DatabaseType,
            Configuration = context.ConfigurationFingerprint ?? $"{context.ConnectionIdentity}:{context.ConfigurationVersion}",
            // Relative database paths resolve against the process working directory.
            WorkingDirectory = Environment.CurrentDirectory,
            Query = query
        })));
    public static string Encode(string binding, DatabaseParameter[] keys)
    {
        var cursor = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new Payload(binding, keys)));
        if (cursor.Length > 32768) throw Invalid("排序键过大，无法生成 cursor。");
        return cursor;
    }

    public static DatabaseParameter[]? Decode(string? cursor, string binding, int count)
    {
        if (cursor == null) return null;
        try
        {
            if (cursor.Length > 32768) throw Invalid("cursor 过长。");
            var payload = JsonSerializer.Deserialize<Payload>(Convert.FromBase64String(cursor));
            if (payload == null || payload.Binding != binding || payload.Keys == null || payload.Keys.Length != count ||
                payload.Keys.Any(k => k == null || k.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined or JsonValueKind.Array or JsonValueKind.Object ||
                    k.DbType == null || k.Direction != DatabaseParameterDirection.Input))
                throw Invalid("cursor 与连接、配置版本或排序条件不匹配。");
            return payload.Keys.Select((k, i) => k with { Name = "seek" + i }).ToArray();
        }
        catch (Exception ex) when (ex is FormatException or JsonException) { throw Invalid("cursor 无效。"); }
    }

    public static string Predicate(string[] columns) => string.Join(" OR ", columns.Select((column, i) =>
        "(" + string.Join(" AND ", columns.Take(i).Select((previous, j) => previous + "=@seek" + j).Append(column + ">@seek" + i)) + ")"));

    public static DatabaseParameter Key(int index, JsonElement value, Type type) => new()
    {
        Name = "seek" + index, Value = value, DbType = type == typeof(Guid) ? DatabaseParameterType.Guid :
            type == typeof(byte[]) ? DatabaseParameterType.Binary : type == typeof(DateTime) ? DatabaseParameterType.DateTime :
            type == typeof(DateTimeOffset) ? DatabaseParameterType.DateTimeOffset : type == typeof(TimeSpan) ? DatabaseParameterType.Time :
            type == typeof(bool) ? DatabaseParameterType.Boolean : type == typeof(decimal) ? DatabaseParameterType.Decimal :
            type == typeof(double) ? DatabaseParameterType.Double : type == typeof(float) ? DatabaseParameterType.Single :
            type == typeof(ulong) ? DatabaseParameterType.Decimal : type.IsPrimitive ? DatabaseParameterType.Int64 : DatabaseParameterType.String
    };

    private static DatabaseMcpException Invalid(string message) => new(DatabaseErrorCode.InvalidParameters, message);
}
