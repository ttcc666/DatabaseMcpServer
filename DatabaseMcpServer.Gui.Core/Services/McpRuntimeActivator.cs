using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DatabaseMcpServer.Gui.Core.Services;

/// <summary>
/// Optionally tells a locally hosted MCP Web API to activate a database after
/// the GUI has saved it as the default connection. The URL is deliberately
/// opt-in through DMS_MCP_WEB_URL; the GUI never scans ports or contacts an
/// unknown local service.
/// </summary>
public sealed class McpRuntimeActivator
{
    public const string WebUrlEnvironmentVariableName = "DMS_MCP_WEB_URL";

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);
    private readonly HttpMessageHandler? _handler;
    private readonly TimeSpan _timeout;

    public McpRuntimeActivator(HttpMessageHandler? handler = null, TimeSpan? timeout = null)
    {
        _handler = handler;
        _timeout = timeout ?? DefaultTimeout;
    }

    public async Task<RuntimeActivationResult> TrySwitchDatabaseAsync(
        string databaseName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            return RuntimeActivationResult.NotAttempted("数据库名称为空。");
        }

        var configuredUrl = Environment.GetEnvironmentVariable(WebUrlEnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(configuredUrl))
        {
            return RuntimeActivationResult.NotAttempted(
                $"未配置 {WebUrlEnvironmentVariableName}，运行中的 MCP Server 需要重新加载配置。");
        }

        if (!Uri.TryCreate(configuredUrl.Trim(), UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            return RuntimeActivationResult.Failed(
                $"{WebUrlEnvironmentVariableName} 不是有效的 HTTP 地址。");
        }

        var endpoint = new Uri(
            baseUri.ToString().TrimEnd('/') + "/api/current-database/switch",
            UriKind.Absolute);

        try
        {
            using var client = _handler == null
                ? new HttpClient { Timeout = _timeout }
                : new HttpClient(_handler, disposeHandler: false) { Timeout = _timeout };
            using var response = await client.PostAsJsonAsync(
                endpoint,
                new { databaseName },
                cancellationToken).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return RuntimeActivationResult.Failed(
                    $"运行时返回 HTTP {(int)response.StatusCode}。{ExtractMessage(payload)}");
            }

            if (!TryReadSuccess(payload, out var success, out var message))
            {
                return RuntimeActivationResult.Failed("运行时返回了无法识别的响应。");
            }

            return success
                ? RuntimeActivationResult.Success(message)
                : RuntimeActivationResult.Failed(message ?? "运行时拒绝切换数据库。");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return RuntimeActivationResult.Failed("运行时激活超时。");
        }
        catch (Exception ex)
        {
            return RuntimeActivationResult.Failed($"无法连接运行中的 MCP Server：{ex.Message}");
        }
    }

    private static bool TryReadSuccess(string payload, out bool success, out string? message)
    {
        success = false;
        message = null;
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("success", out var successProperty)
                || (successProperty.ValueKind != JsonValueKind.True
                    && successProperty.ValueKind != JsonValueKind.False))
            {
                return false;
            }

            success = successProperty.GetBoolean();
            if (root.TryGetProperty("message", out var messageProperty)
                && messageProperty.ValueKind == JsonValueKind.String)
            {
                message = messageProperty.GetString();
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string ExtractMessage(string payload)
    {
        return TryReadSuccess(payload, out _, out var message) && !string.IsNullOrWhiteSpace(message)
            ? $" {message}"
            : string.Empty;
    }
}

public sealed record RuntimeActivationResult(
    bool Attempted,
    bool Succeeded,
    string? Message)
{
    public static RuntimeActivationResult NotAttempted(string message) =>
        new(false, false, message);

    public static RuntimeActivationResult Success(string? message) =>
        new(true, true, message);

    public static RuntimeActivationResult Failed(string message) =>
        new(true, false, message);
}
