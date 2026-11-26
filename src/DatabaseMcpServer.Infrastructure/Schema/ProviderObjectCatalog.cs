using DatabaseMcpServer.Models;
using SqlSugar;

namespace DatabaseMcpServer.Helpers;

internal static class ProviderObjectCatalog
{
    public static IEnumerable<DatabaseObjectInfo> Read(ISqlSugarClient db, string kind, string? tableName)
    {
        if (kind != "trigger" && tableName != null)
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "tableName 仅用于筛选 trigger。");
        if (kind is "table" or "view")
        {
            var tables = kind == "table" ? db.DbMaintenance.GetTableInfoList(false) : db.DbMaintenance.GetViewInfoList(false);
            return tables.Select(table => new DatabaseObjectInfo { Name = table.Name, Identity = table.Name, Kind = kind });
        }
        if (kind == "trigger" && string.IsNullOrWhiteSpace(tableName))
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "此 provider 查询 trigger 必须提供 tableName。");
        if (tableName != null) SqlSafetyGuard.EnsureSafeTableName(tableName);
        var names = kind switch
        {
            "procedure" => db.DbMaintenance.GetProcList(),
            "function" => db.DbMaintenance.GetFuncList(),
            _ => db.DbMaintenance.GetTriggerNames(tableName!)
        };
        return names.Select(name => new DatabaseObjectInfo { Name = name, Identity = name, Kind = kind, TableName = tableName });
    }
}
