using Microsoft.Extensions.Logging;
using SqlSugar;

namespace DatabaseMcpServer.Strategies.DBSetting;

public class GaussDbOptimizationStrategy(ILogger? logger = null, DbType product = DbType.GaussDBNative) : IDatabaseOptimizationStrategy
{
    public void ApplyOptimizations(ConnMoreSettings settings, Dictionary<string, string>? optimizationSettings)
    {
        settings.DisableNvarchar = true;
        if (product != DbType.GaussDBNative) return;

        // DbType selects the driver; DatabaseModel selects the native provider's server dialect.
        settings.DatabaseModel = DbType.GaussDB;
        if (optimizationSettings?.TryGetValue("databaseModel", out var model) == true)
            settings.DatabaseModel = Enum.Parse<DbType>(model, ignoreCase: true);
        else if (optimizationSettings?.TryGetValue("isOpenGauss", out var legacy) == true && bool.TryParse(legacy, out var open))
            settings.DatabaseModel = open ? DbType.OpenGauss : DbType.GaussDB;
        logger?.LogDebug("GaussDB 原生 provider 数据库模式: {Model}", settings.DatabaseModel);
    }

    public string GetDescription() => "GaussDB/OpenGauss：禁用 Nvarchar；原生 provider 数据库模式";
}
