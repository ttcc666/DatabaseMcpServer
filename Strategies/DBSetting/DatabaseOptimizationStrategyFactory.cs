using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies.DBSetting;

/// <summary>
/// 数据库优化策略工厂
/// 使用字典映射 + 策略模式，方便扩展新的数据库类型
/// </summary>
public class DatabaseOptimizationStrategyFactory
{
    private readonly ILogger? _logger;
    private readonly Dictionary<DbType, Func<IDatabaseOptimizationStrategy>> _strategyFactories;

    public DatabaseOptimizationStrategyFactory(ILogger? logger = null)
    {
        _logger = logger;
        _strategyFactories = InitializeStrategyFactories();
    }

    /// <summary>
    /// 初始化策略工厂字典
    /// 添加新数据库优化策略时，只需在此方法中添加映射即可
    /// </summary>
    private Dictionary<DbType, Func<IDatabaseOptimizationStrategy>> InitializeStrategyFactories()
    {
        return new Dictionary<DbType, Func<IDatabaseOptimizationStrategy>>
        {
            // MySQL 系列
            [DbType.MySql] = () => new MySqlOptimizationStrategy(_logger),

            // SQL Server
            [DbType.SqlServer] = () => new SqlServerOptimizationStrategy(_logger),

            // Oracle
            [DbType.Oracle] = () => new OracleOptimizationStrategy(_logger),

            // PostgreSQL
            [DbType.PostgreSQL] = () => new PostgreSqlOptimizationStrategy(_logger),

            // SQLite
            [DbType.Sqlite] = () => new SqliteOptimizationStrategy(_logger),

            // 国产数据库
            [DbType.Dm] = () => new DmOptimizationStrategy(_logger),
            [DbType.Kdbndp] = () => new KdbndpOptimizationStrategy(_logger),
            [DbType.Oscar] = () => new OscarOptimizationStrategy(_logger),
            [DbType.HG] = () => new HighGoOptimizationStrategy(_logger),
            [DbType.Vastbase] = () => new VastbaseOptimizationStrategy(_logger),
            [DbType.GoldenDB] = () => new GoldenDbOptimizationStrategy(_logger),

            // 分布式数据库
            [DbType.OceanBase] = () => new OceanBaseOptimizationStrategy(_logger),
            [DbType.Tidb] = () => new TidbOptimizationStrategy(_logger),
            [DbType.PolarDB] = () => new MySqlOptimizationStrategy(_logger), // PolarDB 兼容 MySQL

            // 时序数据库
            [DbType.ClickHouse] = () => new ClickHouseOptimizationStrategy(_logger),

            // 其他数据库
            [DbType.HANA] = () => new HanaOptimizationStrategy(_logger),
            [DbType.DB2] = () => new Db2OptimizationStrategy(_logger),
            [DbType.MongoDb] = () => new MongoDbOptimizationStrategy(_logger),

            // 特定版本和变体
            [DbType.OpenGauss] = () => new GaussDbOptimizationStrategy(_logger),
            [DbType.GaussDB] = () => new GaussDbOptimizationStrategy(_logger),
            [DbType.GaussDBNative] = () => new GaussDbOptimizationStrategy(_logger)
        };
    }

    /// <summary>
    /// 获取指定数据库类型的优化策略
    /// </summary>
    /// <param name="dbType">数据库类型</param>
    /// <returns>优化策略实例</returns>
    public IDatabaseOptimizationStrategy GetStrategy(DbType dbType)
    {
        if (_strategyFactories.TryGetValue(dbType, out var factory))
        {
            return factory();
        }

        // 如果没有找到对应的策略，返回默认策略
        _logger?.LogWarning("未找到数据库类型 {DbType} 的优化策略，使用默认配置", dbType);
        return new DefaultOptimizationStrategy(dbType.ToString(), _logger);
    }

    /// <summary>
    /// 获取所有支持的数据库类型
    /// </summary>
    public IEnumerable<DbType> GetSupportedDatabaseTypes()
    {
        return _strategyFactories.Keys;
    }

    /// <summary>
    /// 检查是否支持指定的数据库类型
    /// </summary>
    public bool IsSupported(DbType dbType)
    {
        return _strategyFactories.ContainsKey(dbType);
    }
}
