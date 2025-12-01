using SqlSugar;

namespace DatabaseMcpServer.Strategies;

/// <summary>
/// 数据库性能优化策略接口
/// </summary>
public interface IDatabaseOptimizationStrategy
{
    /// <summary>
    /// 应用数据库特定的性能优化配置
    /// </summary>
    /// <param name="settings">连接配置设置</param>
    /// <param name="optimizationSettings">从 JSON 配置读取的优化选项</param>
    void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings);

    /// <summary>
    /// 获取优化策略的描述信息
    /// </summary>
    string GetDescription();
}
