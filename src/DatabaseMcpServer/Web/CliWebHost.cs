using System.Reflection;
using System.Net;
using System.Text.Json;
using DatabaseMcpServer.Configuration;
using DatabaseMcpServer.Extensions;
using DatabaseMcpServer.Hosting;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Operations;
using DatabaseMcpServer.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace DatabaseMcpServer.Web;

internal interface ICliWebHost
{
    Task RunAsync(CliWebCommandOptions options, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken = default);
}

internal sealed class CliWebHost : ICliWebHost
{
    private readonly ICliBrowserLauncher _browserLauncher;
    private readonly string? _currentDatabaseStateFilePath;

    public CliWebHost()
        : this(new CliBrowserLauncher(), null)
    {
    }

    internal CliWebHost(ICliBrowserLauncher browserLauncher, string? currentDatabaseStateFilePath)
    {
        _browserLauncher = browserLauncher;
        _currentDatabaseStateFilePath = currentDatabaseStateFilePath;
    }

    public async Task RunAsync(CliWebCommandOptions options, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken = default)
    {
        await using var handle = await StartAsync(options, cancellationToken);

        await stderr.WriteLineAsync($"Web 配置页已启动: {handle.BaseAddress}");
        await stderr.WriteLineAsync($"配置文件: {handle.ConfigContext.ConfigPath}");
        await stderr.WriteLineAsync($"来源: {handle.ConfigContext.Source}");

        if (options.OpenBrowser)
        {
            if (_browserLauncher.TryOpen(handle.BaseAddress, out var errorMessage))
            {
                await stderr.WriteLineAsync("已尝试打开默认浏览器。");
            }
            else
            {
                await stderr.WriteLineAsync($"打开浏览器失败，请手动访问: {handle.BaseAddress}");
                if (!string.IsNullOrWhiteSpace(errorMessage))
                {
                    await stderr.WriteLineAsync($"原因: {errorMessage}");
                }
            }
        }
        else
        {
            await stderr.WriteLineAsync("已禁用自动打开浏览器，请手动访问上面的地址。");
        }

        await handle.WaitForShutdownAsync(cancellationToken);
    }

    internal async Task<CliWebHostHandle> StartAsync(CliWebCommandOptions options, CancellationToken cancellationToken = default)
    {
        var configContext = CliWebConfigContextResolver.Resolve(options.ConfigPath);

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = [],
            ApplicationName = Assembly.GetExecutingAssembly().GetName().Name,
            ContentRootPath = AppContext.BaseDirectory
        });

        builder.Logging.ClearProviders();

        AddServices(builder.Services, configContext, _currentDatabaseStateFilePath, options.EnableMonitorConfig);
        builder.WebHost.ConfigureKestrel(optionsBuilder =>
        {
            if (options.Port is > 0)
            {
                optionsBuilder.ListenLocalhost(options.Port.Value);
            }
            else
            {
                optionsBuilder.Listen(IPAddress.Loopback, 0);
            }
        });

        var app = builder.Build();
        ConfigureApplication(app);

        try
        {
            await app.StartAsync(cancellationToken);
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }

        return new CliWebHostHandle(
            app,
            ResolveBaseAddress(app),
            configContext);
    }

    private static void ConfigureApplication(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var request = context.Request;
            if (request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
            {
                var error = LocalWebRequestGuard.Validate(request);
                if (error != null)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new { success = false, message = error });
                    return;
                }
            }
            await next(context);
        });

        // Prefer resource-name based lookup over ManifestEmbeddedFileProvider.
        // The embedded-files manifest splits TargetPath with the build OS separator, so a
        // hierarchical "website/dist" scope is not reliable across Windows and Linux CI.
        var fileProvider = new EmbeddedFileProvider(
            Assembly.GetExecutingAssembly(),
            "DatabaseMcpServer.website.dist");

        app.MapCliWebConfigurationEndpoints();
        app.MapCliWebToolEndpoints();

        app.UseDefaultFiles(new DefaultFilesOptions
        {
            FileProvider = fileProvider
        });
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = fileProvider
        });
        app.MapFallback(async context =>
        {
            if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    message = $"未知 API: {context.Request.Path}"
                }));
                return;
            }

            var fileInfo = fileProvider.GetFileInfo("index.html");
            if (!fileInfo.Exists)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsync("Web UI assets are missing.");
                return;
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            await using var stream = fileInfo.CreateReadStream();
            await stream.CopyToAsync(context.Response.Body, context.RequestAborted);
        });
    }

    private static void AddServices(
        IServiceCollection services,
        CliWebConfigContext configContext,
        string? currentDatabaseStateFilePath,
        bool? enableMonitorConfig)
    {
        var logger = DatabaseHostBuilderFactory.CreateLogger(silentLogs: true);
        services.AddSerilog(logger);
        services.AddDatabaseMcpApplicationServices(
            cliToolMode: true,
            currentDatabaseStateFilePath: currentDatabaseStateFilePath,
            enableMonitorConfig: enableMonitorConfig,
            configPath: configContext.ConfigPath);
        services.AddDatabaseMcpToolServices();
        services.AddSingleton(configContext);
        services.AddSingleton<ConfigurationFileStore>();
        services.AddSingleton(services => new ConfigurationCommands(
            services.GetRequiredService<ConfigurationFileStore>(),
            services.GetRequiredService<ISqlSugarClientFactory>(), services.GetRequiredService<DatabaseConnectionProbe>(),
            services.GetRequiredService<DatabaseExecutionCoordinator>()));
        // Resolve runtime services only when requested so missing or invalid configuration remains editable.
        services.AddSingleton(services => new CliWebConfigurationQueries(
            configContext,
            services.GetRequiredService<ConfigurationFileStore>(),
            services.GetRequiredService<ICurrentDatabaseStateStore>(),
            () => services.GetService<IDatabaseConfigService>()));
        services.AddSingleton(services => new CliWebApiService(
            configContext,
            services.GetRequiredService<ConfigurationCommands>(),
            services.GetRequiredService<ConnectionStringBuilder>(),
            services.GetRequiredService<CliWebConfigurationQueries>(),
            services.GetRequiredService<ConfigurationRuntimeApplier>(),
            () => services.GetRequiredService<IDatabaseConfigService>(),
            () => services.GetRequiredService<ConnectionOperations>()));
    }

    private static Uri ResolveBaseAddress(WebApplication app)
    {
        var server = app.Services.GetRequiredService<IServer>();
        var addresses = server.Features
            .Get<IServerAddressesFeature>()?
            .Addresses
            ?? throw new InvalidOperationException("无法获取 Web 服务监听地址。");

        var preferredAddress = addresses.FirstOrDefault(address =>
                                   address.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                                   address.Contains("localhost", StringComparison.OrdinalIgnoreCase))
                               ?? addresses.OrderBy(address => address, StringComparer.OrdinalIgnoreCase).First();

        return new Uri(preferredAddress);
    }

    internal sealed class CliWebHostHandle : IAsyncDisposable
    {
        private readonly WebApplication _application;

        public CliWebHostHandle(
            WebApplication application,
            Uri baseAddress,
            CliWebConfigContext configContext)
        {
            _application = application;
            BaseAddress = baseAddress;
            ConfigContext = configContext;
        }

        public Uri BaseAddress { get; }

        public CliWebConfigContext ConfigContext { get; }

        public Task WaitForShutdownAsync(CancellationToken cancellationToken = default)
        {
            return _application.WaitForShutdownAsync(cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _application.StopAsync();
            }
            finally
            {
                await _application.DisposeAsync();
            }
        }
    }
}
