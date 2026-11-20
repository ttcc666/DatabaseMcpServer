using DatabaseMcpServer.Models;

namespace DatabaseMcpServer.Helpers;

internal enum DatabaseToolAccess { Read, Write, Schema }

internal static class DatabaseAccessPolicy
{
    public static DatabaseToolAccess Parse(string? value) => value?.ToLowerInvariant() switch
    {
        null or "schema-admin" => DatabaseToolAccess.Schema,
        "read-write" => DatabaseToolAccess.Write,
        "read-only" => DatabaseToolAccess.Read,
        _ => throw new ArgumentException("accessMode 必须是 read-only、read-write 或 schema-admin。")
    };

    public static void EnsureAllowed(DatabaseOperationContext context, DatabaseToolAccess required, DatabaseToolAccess serverAccess)
    {
        if (required > serverAccess || required > Parse(context.AccessMode))
            throw new DatabaseMcpException(DatabaseErrorCode.AccessDenied, "当前执行策略不允许此类数据库操作。");
    }

}
