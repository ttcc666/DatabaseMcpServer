using DatabaseMcpServer.Extensions;
using DatabaseMcpServer.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DatabaseMcpServer.Tests;

public class CompositionTests
{
    [Fact]
    public void HostBuilder_ShouldRegisterCoreServices_AndBuild()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddDatabaseMcpApplicationServices();
        builder.Services.AddDatabaseMcpServer();

        using var host = builder.Build();

        Assert.NotNull(host.Services.GetRequiredService<IJsonResultSerializer>());
        Assert.NotNull(host.Services.GetRequiredService<IDatabaseHelperService>());
        Assert.NotNull(host.Services.GetRequiredService<ISqlSugarClientFactory>());
    }
}
