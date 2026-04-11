using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Text.Json;
using DatabaseMcpServer.Extensions;
using ModelContextProtocol.Server;

namespace DatabaseMcpServer.Cli;

internal sealed record CliToolParameterMetadata(
    string ParameterName,
    string OptionName,
    string Description,
    Type ParameterType,
    bool IsRequired,
    object? DefaultValue)
{
    public Type EffectiveType => Nullable.GetUnderlyingType(ParameterType) ?? ParameterType;

    public bool IsBoolean => EffectiveType == typeof(bool);

    public bool IsJson => EffectiveType == typeof(JsonElement);

    public string DisplayTypeName => EffectiveType == typeof(string)
        ? "string"
        : EffectiveType == typeof(int)
            ? "int"
            : EffectiveType == typeof(bool)
                ? "bool"
                : EffectiveType == typeof(JsonElement)
                    ? "json"
                    : EffectiveType.Name;
}

internal sealed record CliToolMetadata(
    string Name,
    string Description,
    Type ToolType,
    MethodInfo Method,
    IReadOnlyList<CliToolParameterMetadata> Parameters,
    bool RequiresConfirmation);

internal sealed class CliToolCatalog
{
    private readonly IReadOnlyList<CliToolMetadata> _toolList;
    private readonly IReadOnlyDictionary<string, CliToolMetadata> _tools;

    public CliToolCatalog()
    {
        _toolList = DatabaseMcpToolCatalog.ToolTypes
            .SelectMany(BuildToolMetadata)
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();

        _tools = _toolList.ToDictionary(item => item.Name, StringComparer.Ordinal);
    }

    public IReadOnlyCollection<CliToolMetadata> Tools => _toolList;

    public bool TryGetTool(string toolName, out CliToolMetadata metadata)
    {
        return _tools.TryGetValue(toolName, out metadata!);
    }

    public IReadOnlyList<string> GetClosestToolNames(string toolName, int count = 3)
    {
        return _tools.Keys
            .OrderBy(name => GetDistance(toolName, name))
            .ThenBy(name => name, StringComparer.Ordinal)
            .Take(count)
            .ToArray();
    }

    private static IEnumerable<CliToolMetadata> BuildToolMetadata(Type toolType)
    {
        return toolType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() != null)
            .Select(method =>
            {
                var toolName = ToSnakeCase(method.Name);
                var parameters = method
                    .GetParameters()
                    .Select(parameter => new CliToolParameterMetadata(
                        parameter.Name ?? string.Empty,
                        ToKebabCase(parameter.Name ?? string.Empty),
                        parameter.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty,
                        parameter.ParameterType,
                        !parameter.HasDefaultValue,
                        parameter.HasDefaultValue ? parameter.DefaultValue : null))
                    .ToArray();

                return new CliToolMetadata(
                    toolName,
                    method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty,
                    toolType,
                    method,
                    parameters,
                    CliWriteProtection.RequiresConfirmation(toolName));
            });
    }

    internal static string ToSnakeCase(string value)
    {
        return ConvertCase(value, '_');
    }

    internal static string ToKebabCase(string value)
    {
        return ConvertCase(value, '-');
    }

    private static string ConvertCase(string value, char separator)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (char.IsUpper(current))
            {
                var hasPrevious = i > 0;
                var previousIsLower = hasPrevious && char.IsLower(value[i - 1]);
                var previousIsDigit = hasPrevious && char.IsDigit(value[i - 1]);
                var nextIsLower = i + 1 < value.Length && char.IsLower(value[i + 1]);
                if (hasPrevious && (previousIsLower || previousIsDigit || nextIsLower))
                {
                    builder.Append(separator);
                }
            }

            builder.Append(char.ToLowerInvariant(current));
        }

        return builder.ToString();
    }

    private static int GetDistance(string left, string right)
    {
        var costs = new int[right.Length + 1];
        for (var j = 0; j <= right.Length; j++)
        {
            costs[j] = j;
        }

        for (var i = 1; i <= left.Length; i++)
        {
            var previousDiagonal = costs[0];
            costs[0] = i;

            for (var j = 1; j <= right.Length; j++)
            {
                var temp = costs[j];
                var substitutionCost = left[i - 1] == right[j - 1] ? 0 : 1;
                costs[j] = Math.Min(
                    Math.Min(costs[j] + 1, costs[j - 1] + 1),
                    previousDiagonal + substitutionCost);
                previousDiagonal = temp;
            }
        }

        return costs[right.Length];
    }
}
