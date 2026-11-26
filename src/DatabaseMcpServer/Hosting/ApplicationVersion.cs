using System.Reflection;

namespace DatabaseMcpServer.Hosting;

internal static class ApplicationVersion
{
    public static string Value { get; } = typeof(ApplicationVersion).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
}
