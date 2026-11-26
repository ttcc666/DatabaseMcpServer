using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DatabaseMcpServer.Web;

internal static class CliWebConfigurationEndpoints
{
    public static void MapCliWebConfigurationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/context", (CliWebApiService service) => Results.Json(service.GetContext()));
        app.MapGet("/api/dashboard", (CliWebApiService service) => Results.Json(service.GetDashboard()));
        app.MapGet("/api/presets", (CliWebApiService service) => Results.Json(service.GetPresets()));
        app.MapGet("/api/presets/{dbType}", (CliWebApiService service, string dbType) => Results.Json(service.GetPreset(dbType)));
        app.MapGet("/api/connection-string-profiles/{dbType}", (CliWebApiService service, string dbType) => Results.Json(service.GetConnectionStringProfile(dbType)));
        app.MapGet("/api/databases/{name}", (CliWebApiService service, string name) => Results.Json(service.GetDatabase(name)));

        app.MapPost("/api/config/init", (CliWebApiService service, CliWebInitializeRequest request) =>
            JsonPayload(service.Initialize(request.Force)));
        app.MapPost("/api/config/validate", (CliWebApiService service) =>
            JsonPayload(service.Validate()));
        app.MapPost("/api/config/doctor", async (CliWebApiService service, CliWebDoctorRequest request, CancellationToken cancellationToken) =>
            JsonPayload(await service.DoctorAsync(request, cancellationToken)));
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
        app.MapPost("/api/databases/{name}/test", async (CliWebApiService service, string name, CancellationToken cancellationToken) =>
            JsonPayload(await service.TestConnectionAsync(name, cancellationToken)));
        app.MapPost("/api/databases/health-check", async (CliWebApiService service, CancellationToken cancellationToken) =>
            JsonPayload(await service.HealthCheckAsync(cancellationToken)));
        app.MapPost("/api/current-database/switch", (CliWebApiService service, CliWebSwitchCurrentDatabaseRequest request) =>
            JsonPayload(service.SwitchCurrentDatabase(request.DatabaseName)));
    }

    private static IResult JsonPayload(string payload)
    {
        return Results.Content(payload, "application/json; charset=utf-8");
    }
}
