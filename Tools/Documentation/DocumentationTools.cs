using DatabaseMcpServer.Filters;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;
using System.Text.Json;

namespace DatabaseMcpServer.Tools.Documentation;

/// <summary>
/// 一步生成数据库文档的工具
/// </summary>
public class DocumentationTools
{
    private readonly IDatabaseDocumentationService _documentationService;
    private readonly IDatabaseHelperService _databaseHelper;
    private readonly ILogger<DocumentationTools> _logger;

    public DocumentationTools(
        IDatabaseDocumentationService documentationService,
        IDatabaseHelperService databaseHelper,
        ILogger<DocumentationTools> logger)
    {
        _documentationService = documentationService;
        _databaseHelper = databaseHelper;
        _logger = logger;
    }

    [McpServerTool]
    [Description("Generate full database documentation (tables, columns, indexes, triggers, views) in a single call.")]
    public string GenerateDatabaseDocumentation(
        [Description("Output format: markdown or json (default: markdown)")] string format = "markdown",
        [Description("Optional connection name from databases.json; defaults to the current connection.")] string? connectionName = null,
        [Description("Optional comma-separated table names to include; defaults to all tables.")] string? tableNames = null,
        [Description("Return mode: content | base64 | path (default: content). base64/path will write a temp file when filePath is not provided.")] string returnMode = "content",
        [Description("Optional output file path (used when returnMode is base64 or path).")] string? filePath = null)
    {
        try
        {
            var normalizedFormat = string.IsNullOrWhiteSpace(format) ? "markdown" : format.Trim().ToLowerInvariant();
            if (normalizedFormat is not ("markdown" or "json"))
            {
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "format 仅支持 markdown 或 json");
            }

            var normalizedReturnMode = string.IsNullOrWhiteSpace(returnMode) ? "content" : returnMode.Trim().ToLowerInvariant();
            if (normalizedReturnMode is not ("content" or "base64" or "path"))
            {
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "returnMode 仅支持 content、base64 或 path");
            }

            var tableFilters = ParseTableFilters(tableNames);
            var documentation = _documentationService.GenerateDocumentation(connectionName, tableFilters);

            if (normalizedReturnMode == "content")
            {
                if (normalizedFormat == "json")
                {
                    return _databaseHelper.SerializeResult(new
                    {
                        success = true,
                        format = normalizedFormat,
                        connection = documentation.ConnectionName,
                        data = documentation
                    });
                }

                var markdown = DocumentationMarkdownFormatter.ToMarkdown(documentation);

                return _databaseHelper.SerializeResult(new
                {
                    success = true,
                    format = normalizedFormat,
                    connection = documentation.ConnectionName,
                    generatedAtUtc = documentation.GeneratedAtUtc,
                    tables = documentation.Tables.Count,
                    views = documentation.Views.Count,
                    content = markdown,
                    warnings = documentation.Warnings
                });
            }

            var (actualPath, base64Content) = WriteDocumentationToFile(documentation, normalizedFormat, filePath, normalizedReturnMode);

            return _databaseHelper.SerializeResult(new
            {
                success = true,
                format = normalizedFormat,
                connection = documentation.ConnectionName,
                generatedAtUtc = documentation.GeneratedAtUtc,
                tables = documentation.Tables.Count,
                views = documentation.Views.Count,
                warnings = documentation.Warnings,
                returnMode = normalizedReturnMode,
                filePath = actualPath,
                fileName = Path.GetFileName(actualPath),
                base64 = normalizedReturnMode == "base64" ? base64Content : null
            });
        }
        catch (Exception ex)
        {
            return McpExceptionFilter.HandleException(ex, _logger);
        }
    }

    private static IReadOnlyCollection<string>? ParseTableFilters(string? tableNames)
    {
        if (string.IsNullOrWhiteSpace(tableNames))
        {
            return null;
        }

        var names = tableNames
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();

        return names.Count == 0 ? null : names;
    }

    private (string Path, string? Base64) WriteDocumentationToFile(
        DatabaseDocumentation documentation,
        string format,
        string? filePath,
        string returnMode)
    {
        var extension = format == "json" ? ".json" : ".md";
        var actualPath = string.IsNullOrWhiteSpace(filePath)
            ? Path.Combine(Path.GetTempPath(), $"DatabaseDoc_{SanitizeFileName(documentation.ConnectionName)}_{DateTime.UtcNow:yyyyMMdd_HHmmss}{extension}")
            : filePath!;

        var directory = Path.GetDirectoryName(actualPath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (format == "json")
        {
            var json = JsonSerializer.Serialize(documentation, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });
            File.WriteAllText(actualPath, json, Encoding.UTF8);
        }
        else
        {
            var markdown = DocumentationMarkdownFormatter.ToMarkdown(documentation);
            File.WriteAllText(actualPath, markdown, Encoding.UTF8);
        }

        if (returnMode == "base64")
        {
            var bytes = File.ReadAllBytes(actualPath);
            var base64 = Convert.ToBase64String(bytes);
            return (actualPath, base64);
        }

        return (actualPath, null);
    }

    private static string SanitizeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "database";
        }

        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return name;
    }
}