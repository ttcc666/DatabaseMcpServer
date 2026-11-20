using DatabaseMcpServer.Helpers;
using Serilog;
using SqlSugar;
using System.Reflection;

namespace DatabaseMcpServer.Services;

internal static class SqlSugarProviderWarmup
{
    private static readonly Lazy<Assembly[]> Installed = new(() =>
    {
        var assemblies = DatabaseProviderCatalog.ExtensionAssemblies
            .Where(pair => DatabaseProviderCatalog.UnavailableReason(pair.Key) == null)
            .Select(pair => Assembly.Load(pair.Value)).ToArray();
        InstanceFactory.CustomAssemblies = InstanceFactory.CustomAssemblies.Concat(assemblies).Distinct().ToArray();
        return assemblies;
    });

    public static void Warmup(ILogger? logger)
    {
        foreach (var assembly in Installed.Value)
            logger?.Debug("已注册 SqlSugar 扩展程序集: {Assembly}", assembly.GetName().Name);
    }
}
