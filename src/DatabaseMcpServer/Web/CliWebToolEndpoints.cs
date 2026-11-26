using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DatabaseMcpServer.Web;

internal static class CliWebToolEndpoints
{
    public static void MapCliWebToolEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/tools", (CliWebToolService service) => Results.Json(service.GetTools()));

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

    private sealed class ToolRequestBodyTooLargeException(string message) : Exception(message);
}
