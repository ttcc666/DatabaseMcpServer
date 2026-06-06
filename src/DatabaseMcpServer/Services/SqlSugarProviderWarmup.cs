using Serilog;
using System.Reflection;

namespace DatabaseMcpServer.Services;

internal static class SqlSugarProviderWarmup
{
    public static void Warmup(ILogger? logger)
    {
        var providers = new (string AssemblyName, string? ProviderTypeName)[]
        {
            ("SqlSugar.ClickHouseCore", "ClickHouseProvider"),
            ("SqlSugar.MongoDbCore", "MongoDbProvider"),
            ("SqlSugar.GaussDBCore", "SqlSugar.GaussDBCore.GaussDBDataAdapter"),
            ("SqlSugar.OceanBaseForOracleCore", "OceanBaseForOracleProvider")
        };

        foreach (var (assemblyName, providerTypeName) in providers)
        {
            try
            {
                var assembly = Assembly.Load(assemblyName);
                if (providerTypeName == null)
                {
                    logger?.Debug("预加载 SqlSugar 程序集: {Assembly}", assemblyName);
                    continue;
                }

                var providerType = assembly.GetType(providerTypeName, throwOnError: false, ignoreCase: true)
                    ?? assembly
                        .GetTypes()
                        .FirstOrDefault(type =>
                            string.Equals(type.Name, providerTypeName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(type.FullName, providerTypeName, StringComparison.OrdinalIgnoreCase));

                if (providerType != null)
                {
                    _ = providerType.Assembly;
                    logger?.Debug("预加载 SqlSugar Provider: {Provider} ({Assembly})", providerTypeName, assemblyName);
                }
                else
                {
                    logger?.Warning("未找到 Provider 类型 {Provider} 于 {Assembly}", providerTypeName, assemblyName);
                }
            }
            catch (Exception ex)
            {
                logger?.Warning(ex, "预加载 SqlSugar Provider 失败: {Assembly}", assemblyName);
            }
        }
    }
}
