using DatabaseMcpServer.Strategies.DBSetting;
using SqlSugar;

namespace DatabaseMcpServer.Interfaces;

public interface IDatabaseOptimizationStrategyFactory
{
    IDatabaseOptimizationStrategy GetStrategy(DbType dbType);
}
