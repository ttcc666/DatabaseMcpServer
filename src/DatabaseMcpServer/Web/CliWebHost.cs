using System.Reflection;
using System.Text.Json;
using System.Net;
using DatabaseMcpServer.Cli;
using DatabaseMcpServer.Extensions;
using DatabaseMcpServer.Hosting;
using DatabaseMcpServer.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
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
        var originalConfigPath = Environment.GetEnvironmentVariable("DB_CONFIG_PATH");
        Environment.SetEnvironmentVariable("DB_CONFIG_PATH", configContext.ConfigPath);

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = [],
            ApplicationName = Assembly.GetExecutingAssembly().GetName().Name,
            ContentRootPath = AppContext.BaseDirectory
        });

        builder.Logging.ClearProviders();

        var logger = DatabaseHostBuilderFactory.CreateLogger(silentLogs: true);
        builder.Services.AddSerilog(logger);
        SqlSugarProviderWarmup.Warmup(logger);
        builder.Services.AddDatabaseMcpApplicationServices(
            cliToolMode: true,
            currentDatabaseStateFilePath: _currentDatabaseStateFilePath);
        builder.Services.AddDatabaseMcpToolServices();
        builder.Services.AddSingleton(configContext);
        builder.Services.AddSingleton<CliConfigFileService>();
        builder.Services.AddSingleton<CliConfigCommandHandler>();
        builder.Services.AddSingleton<CliWebApiService>();
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
            Environment.SetEnvironmentVariable("DB_CONFIG_PATH", originalConfigPath);
            throw;
        }

        return new CliWebHostHandle(
            app,
            ResolveBaseAddress(app),
            configContext,
            originalConfigPath);
    }

    private static void ConfigureApplication(WebApplication app)
    {
        var fileProvider = new ManifestEmbeddedFileProvider(Assembly.GetExecutingAssembly(), "website/dist");

        app.MapGet("/api/context", (CliWebApiService service) => Results.Json(service.GetContext()));
        app.MapGet("/api/dashboard", (CliWebApiService service) => Results.Json(service.GetDashboard()));
        app.MapGet("/api/presets", (CliWebApiService service) => Results.Json(service.GetPresets()));
        app.MapGet("/api/presets/{dbType}", (CliWebApiService service, string dbType) => Results.Json(service.GetPreset(dbType)));
        app.MapGet("/api/connection-string-profiles/{dbType}", (CliWebApiService service, string dbType) => Results.Json(service.GetConnectionStringProfile(dbType)));
        app.MapGet("/api/databases/{name}", (CliWebApiService service, string name) => Results.Json(service.GetDatabase(name)));
        app.MapGet("/api/tools", (CliWebToolService service) => Results.Json(service.GetTools()));

        app.MapPost("/api/config/init", (CliWebApiService service, CliWebInitializeRequest request) =>
            JsonPayload(service.Initialize(request.Force)));
        app.MapPost("/api/config/validate", (CliWebApiService service) =>
            JsonPayload(service.Validate()));
        app.MapPost("/api/config/doctor", async (CliWebApiService service, CliWebDoctorRequest request) =>
            JsonPayload(await service.DoctorAsync(request)));
        app.MapGet("/api/config/export", (CliWebApiService service) =>
        {
            var export = service.Export();
            return Results.File(export.Contents, "application/json", export.DownloadName);
        });
        app.MapPost("/api/config/import", async (HttpRequest request, CliWebApiService service, CancellationToken cancellationToken) =>
        {
            var form = await request.ReadFormAsync(cancellationToken);
            var file = form.Files["file"];
            if (file == null || file.Length == 0)
            {
                return JsonPayload(JsonSerializer.Serialize(new
                {
                    success = false,
                    message = "请选择要导入的 JSON 文件。"
                }));
            }

            var force = bool.TryParse(form["force"], out var forceValue) && forceValue;
            await using var fileStream = file.OpenReadStream();
            return JsonPayload(await service.ImportAsync(fileStream, force, cancellationToken));
        });

        app.MapPost("/api/databases/from-preset", (CliWebApiService service, CliWebCreateFromPresetRequest request) =>
            JsonPayload(service.CreateFromPreset(request)));
        app.MapPost("/api/databases", (CliWebApiService service, CliWebAddDatabaseRequest request) =>
            JsonPayload(service.AddDatabase(request)));
        app.MapPost("/api/databases/{name}/rename", (CliWebApiService service, string name, CliWebRenameDatabaseRequest request) =>
            JsonPayload(service.RenameDatabase(name, request)));
        app.MapPut("/api/databases/{name}", (CliWebApiService service, string name, CliWebUpdateDatabaseRequest request) =>
            JsonPayload(service.UpdateDatabase(name, request)));
        app.MapPost("/api/databases/{name}/clone", (CliWebApiService service, string name, CliWebCloneDatabaseRequest request) =>
            JsonPayload(service.CloneDatabase(name, request)));
        app.MapDelete("/api/databases/{name}", (CliWebApiService service, string name) =>
            JsonPayload(service.RemoveDatabase(name)));
        app.MapPost("/api/databases/{name}/set-default", (CliWebApiService service, string name) =>
            JsonPayload(service.SetDefaultDatabase(name)));
        app.MapPost("/api/databases/{name}/test", async (CliWebApiService service, string name) =>
            JsonPayload(await service.TestConnectionAsync(name)));
        app.MapPost("/api/databases/health-check", (CliWebApiService service) =>
            JsonPayload(service.HealthCheck()));
        app.MapPost("/api/current-database/switch", (CliWebApiService service, CliWebSwitchCurrentDatabaseRequest request) =>
            JsonPayload(service.SwitchCurrentDatabase(request.DatabaseName)));
        app.MapPost("/api/tools/{toolName}/invoke", async (HttpRequest request, CliWebToolService service, string toolName, CancellationToken cancellationToken) =>
        {
            var requestError = ValidateToolInvocationRequest(request);
            if (requestError != null)
            {
                return requestError;
            }

            try
            {
                var payload = await ReadToolInvocationRequestAsync(request, cancellationToken);
                return Results.Json(await service.InvokeAsync(toolName, payload, cancellationToken));
            }
            catch (ToolRequestBodyTooLargeException ex)
            {
                return Results.Json(new { success = false, message = ex.Message }, statusCode: StatusCodes.Status413PayloadTooLarge);
            }
            catch (JsonException ex)
            {
                return Results.Json(new { success = false, message = $"Tool 请求 JSON 无效: {ex.Message}" }, statusCode: StatusCodes.Status400BadRequest);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.Json(new { success = false, message = ex.Message }, statusCode: StatusCodes.Status404NotFound);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { success = false, message = ex.Message }, statusCode: StatusCodes.Status400BadRequest);
            }
        });

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

    private static IResult JsonPayload(string payload)
    {
        return Results.Content(payload, "application/json; charset=utf-8");
    }

    private static IResult? ValidateToolInvocationRequest(HttpRequest request)
    {
        if (request.ContentLength is > 1024 * 1024)
        {
            return Results.Json(new { success = false, message = "Tool 请求体不能超过 1 MiB。" }, statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        if (!request.HasJsonContentType())
        {
            return Results.Json(new { success = false, message = "Tool 请求必须使用 application/json。" }, statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        if (!request.Headers.TryGetValue("X-DatabaseMcp-Web", out var marker) || marker != "1")
        {
            return Results.Json(new { success = false, message = "缺少本地 Web 请求标记。" }, statusCode: StatusCodes.Status403Forbidden);
        }

        if (request.Headers.TryGetValue("Origin", out var originValues) && originValues.Count > 0)
        {
            if (originValues.Count != 1 || !Uri.TryCreate(originValues[0], UriKind.Absolute, out var origin))
            {
                return Results.Json(new { success = false, message = "Tool 请求 Origin 无效。" }, statusCode: StatusCodes.Status403Forbidden);
            }

            var requestPort = request.Host.Port ?? (request.IsHttps ? 443 : 80);
            var originPort = origin.IsDefaultPort
                ? string.Equals(origin.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80
                : origin.Port;
            var isSameHost = string.Equals(origin.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(origin.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase) &&
                             originPort == requestPort;
            if (!isSameHost)
            {
                return Results.Json(new { success = false, message = "拒绝跨来源 Tool 请求。" }, statusCode: StatusCodes.Status403Forbidden);
            }
        }

        return null;
    }

    private static async Task<CliWebToolInvocationRequest> ReadToolInvocationRequestAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        const int maximumBodySize = 1024 * 1024;
        await using var body = new MemoryStream();
        var buffer = new byte[64 * 1024];

        while (true)
        {
            var bytesRead = await request.Body.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            if (body.Length + bytesRead > maximumBodySize)
            {
                throw new ToolRequestBodyTooLargeException("Tool 请求体不能超过 1 MiB。");
            }

            await body.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }

        var payload = JsonSerializer.Deserialize<CliWebToolInvocationRequest>(
            body.GetBuffer().AsSpan(0, checked((int)body.Length)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return payload ?? throw new JsonException("请求体不能为空。");
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
        private readonly string? _originalConfigPath;

        public CliWebHostHandle(
            WebApplication application,
            Uri baseAddress,
            CliWebConfigContext configContext,
            string? originalConfigPath)
        {
            _application = application;
            BaseAddress = baseAddress;
            ConfigContext = configContext;
            _originalConfigPath = originalConfigPath;
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
                Environment.SetEnvironmentVariable("DB_CONFIG_PATH", _originalConfigPath);
            }
        }
    }

    private sealed class ToolRequestBodyTooLargeException(string message) : Exception(message);

}
