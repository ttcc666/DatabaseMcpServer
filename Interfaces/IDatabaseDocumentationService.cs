using DatabaseMcpServer.Models;

namespace DatabaseMcpServer.Interfaces;

/// <summary>
/// 数据库文档生成服务接口
/// </summary>
public interface IDatabaseDocumentationService
{
    /// <summary>
    /// 生成数据库文档模型
    /// </summary>
    /// <param name="connectionName">可选的连接名称，默认为当前连接</param>
    /// <param name="tableFilters">可选的表名过滤列表（忽略大小写）</param>
    /// <returns>数据库文档模型</returns>
    DatabaseDocumentation GenerateDocumentation(string? connectionName = null, IReadOnlyCollection<string>? tableFilters = null);
}