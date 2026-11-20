using DatabaseMcpServer.Models;

namespace DatabaseMcpServer.Helpers;

internal static class SqlAccessPolicy
{
    public static void EnsureCommandAllowed(DatabaseClientContext context, string sql, DatabaseToolAccess serverAccess)
    {
        DatabaseAccessPolicy.EnsureAllowed(context, DatabaseToolAccess.Write, serverAccess);
        if (serverAccess < DatabaseToolAccess.Schema || DatabaseAccessPolicy.Parse(context.AccessMode) < DatabaseToolAccess.Schema)
            SqlSafetyGuard.EnsureDataModificationSql(sql);
    }
}
