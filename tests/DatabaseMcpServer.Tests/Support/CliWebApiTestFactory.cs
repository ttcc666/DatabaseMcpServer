using DatabaseMcpServer.Configuration;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Operations;
using DatabaseMcpServer.Services;
using DatabaseMcpServer.Web;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseMcpServer.Tests;

internal static class CliWebApiTestFactory
{
    public static CliWebApiService Create(
        CliWebConfigContext context,
        ConfigurationFileStore files,
        ConfigurationCommands commands,
        ICurrentDatabaseStateStore state,
        ConnectionStringBuilder connectionStrings,
        IServiceProvider services)
        => new(context, commands, connectionStrings,
            new CliWebConfigurationQueries(context, files, state, () => services.GetService<IDatabaseConfigService>()),
            services.GetRequiredService<ConfigurationRuntimeApplier>(),
            () => services.GetRequiredService<IDatabaseConfigService>(),
            () => services.GetRequiredService<ConnectionOperations>());
}
