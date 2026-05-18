using DatabaseMcpServer.Cli;

namespace DatabaseMcpServer.Web;

internal static class CliWebConfigContextResolver
{
    public static CliWebConfigContext Resolve(string? explicitConfigPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitConfigPath))
        {
            return new CliWebConfigContext(Path.GetFullPath(explicitConfigPath), "--config");
        }

        var existingResolution = CliConfigurationPathResolver.Resolve(null);
        if (existingResolution.Success)
        {
            return new CliWebConfigContext(existingResolution.Path!, existingResolution.Source!);
        }

        var writableResolution = CliConfigurationPathResolver.ResolveWritablePath(null);
        if (!writableResolution.Success)
        {
            throw new InvalidOperationException(writableResolution.ErrorMessage);
        }

        return new CliWebConfigContext(writableResolution.Path!, writableResolution.Source!);
    }
}
