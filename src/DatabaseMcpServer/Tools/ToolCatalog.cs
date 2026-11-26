using System.ComponentModel;
using System.Reflection;
using System.Text;
using DatabaseMcpServer.Extensions;
using ModelContextProtocol.Server;
using DatabaseMcpServer.Helpers;

namespace DatabaseMcpServer.Tools;

internal sealed record ToolParameterDescriptor(
    string ParameterName,
    string OptionName,
    string Description,
    Type ParameterType,
    bool IsRequired,
    object? DefaultValue,
    bool AllowsNull = false,
    IReadOnlyList<string>? Choices = null)
{
    public Type EffectiveType => Nullable.GetUnderlyingType(ParameterType) ?? ParameterType;

    public bool IsBoolean => EffectiveType == typeof(bool);

    public bool IsJson => EffectiveType.IsArray || EffectiveType == typeof(System.Text.Json.JsonElement) || EffectiveType == typeof(Dictionary<string, System.Text.Json.JsonElement>) || EffectiveType == typeof(DatabaseMcpServer.Models.ColumnDefinition);

    public string DisplayTypeName => EffectiveType == typeof(string)
        ? "string"
        : EffectiveType == typeof(int)
            ? "int"
            : EffectiveType == typeof(bool)
                ? "bool"
                : IsJson
                    ? "json"
                    : EffectiveType.Name;
}

internal sealed record ToolDescriptor(
    string Name,
    string Description,
    Type ToolType,
    MethodInfo Method,
    IReadOnlyList<ToolParameterDescriptor> Parameters,
    bool RequiresConfirmation)
{
    private ToolGroupAttribute Group { get; } = ToolType.GetCustomAttribute<ToolGroupAttribute>()
        ?? throw new InvalidOperationException($"Tool group metadata missing for {ToolType.Name}.");
    private ToolPolicyAttribute? Policy { get; } = Method.GetCustomAttribute<ToolPolicyAttribute>();
    public bool IsCollection => Provider == "mongo";
    public string Provider => Group.Provider;
    public string Category => Group.Category;
    public bool Analysis => Policy?.Analysis == true;
    public DatabaseCapability? Capability => Policy is { Capability: >= 0 } policy ? policy.Capability : null;
    public string[] SupportedProviders => ProviderCapabilities.SupportedProviders(Capability);
    public string[] UnsupportedProviders => ProviderCapabilities.UnsupportedProviders(Capability);
    public DatabaseToolAccess RequiredAccess => Policy is { Access: >= DatabaseToolAccess.Read } policy ? policy.Access : Group.Access;
    public string RequiredAccessName => RequiredAccess switch { DatabaseToolAccess.Read => "read-only", DatabaseToolAccess.Write => "read-write", _ => "schema-admin" };

    private readonly Lazy<ModelContextProtocol.Protocol.Tool> _protocolTool = new(() => ToolInputSchema.Apply(McpServerTool.Create(Method,
        (ModelContextProtocol.Server.RequestContext<ModelContextProtocol.Protocol.CallToolRequestParams> _) =>
            throw new InvalidOperationException("Metadata-only tool cannot be invoked."), new() { Name = Name }).ProtocolTool, Method));
    public ModelContextProtocol.Protocol.Tool ProtocolTool => _protocolTool.Value;

    public object Describe() => new
    {
        name = Name, description = Description, category = Category, provider = Provider,
        requiredAccess = RequiredAccessName,
        requiresConfirmation = RequiresConfirmation, analysis = Analysis, supportedProviders = SupportedProviders,
        unsupportedProviders = UnsupportedProviders, capability = Capability?.ToString(),
        inputSchema = ProtocolTool.InputSchema, outputSchema = ProtocolTool.OutputSchema,
        parameters = Parameters.Select(p => new { name = p.ParameterName, optionName = p.OptionName, required = p.IsRequired, defaultValue = p.DefaultValue })
    };
}

internal sealed class ToolCatalog
{
    private readonly Lazy<ToolDescriptor[]> _toolList = new(() => DatabaseMcpToolCatalog.ToolTypes
        .SelectMany(BuildToolMetadata)
        .OrderBy(item => item.Name, StringComparer.Ordinal)
        .ToArray());
    private readonly Lazy<IReadOnlyDictionary<string, ToolDescriptor>> _tools;

    public ToolCatalog()
    {
        _tools = new(() => _toolList.Value.ToDictionary(item => item.Name, StringComparer.Ordinal));
    }

    public IReadOnlyCollection<ToolDescriptor> Tools => _toolList.Value;

    public bool TryGetTool(string toolName, out ToolDescriptor metadata)
    {
        return _tools.Value.TryGetValue(toolName, out metadata!);
    }

    public IReadOnlyList<string> GetClosestToolNames(string toolName, int count = 3)
    {
        return _tools.Value.Keys
            .OrderBy(name => GetDistance(toolName, name))
            .ThenBy(name => name, StringComparer.Ordinal)
            .Take(count)
            .ToArray();
    }

    private static IEnumerable<ToolDescriptor> BuildToolMetadata(Type toolType)
    {
        return toolType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() != null)
            .Select(method =>
            {
                var toolName = method.GetCustomAttribute<McpServerToolAttribute>()!.Name ?? ToSnakeCase(method.Name);
                var parameters = method
                    .GetParameters()
                    .Where(parameter => parameter.ParameterType != typeof(CancellationToken) &&
                        parameter.ParameterType != typeof(IProgress<ModelContextProtocol.ProgressNotificationValue>))
                    .Select(parameter => new ToolParameterDescriptor(
                        parameter.Name ?? string.Empty,
                        ToKebabCase(parameter.Name ?? string.Empty),
                        parameter.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty,
                        parameter.ParameterType,
                        !parameter.HasDefaultValue,
                        parameter.HasDefaultValue ? parameter.DefaultValue : null,
                        new NullabilityInfoContext().Create(parameter).ReadState == NullabilityState.Nullable,
                        parameter.GetCustomAttribute<ToolChoicesDescriptionAttribute>()?.Choices))
                    .ToArray();

                return new ToolDescriptor(
                    toolName,
                    method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty,
                    toolType,
                    method,
                    parameters,
                    method.GetCustomAttribute<ToolPolicyAttribute>()?.RequiresConfirmation == true);
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
