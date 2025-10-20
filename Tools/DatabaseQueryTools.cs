using System.ComponentModel;
using ModelContextProtocol.Server;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Services;

namespace DatabaseMcpServer.Tools;

/// <summary>
/// 数据库查询工具类，提供基础查询功能。
/// 注意：复杂查询请使用 AdvancedSqlTools 类中的方法。
/// </summary>
internal class DatabaseQueryTools
{
    // 原有的 ExecuteQuery 和 SimpleQuery 方法已移除，
    // 因为 AdvancedSqlTools.SqlQuery 提供了更高效的实现。
    //
    // 如需查询功能，请使用：
    // - AdvancedSqlTools.SqlQuery - 通用查询
    // - AdvancedSqlTools.SqlQuerySingle - 单条记录查询
    // - AdvancedSqlTools.GetScalar - 标量值查询
    // - AdvancedSqlTools.GetString/GetInt/GetLong 等 - 类型化查询
}
