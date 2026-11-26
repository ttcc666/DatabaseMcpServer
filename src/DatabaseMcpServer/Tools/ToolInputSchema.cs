using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace DatabaseMcpServer.Tools;

internal static class ToolInputSchema
{
    public static Tool Apply(Tool tool, MethodInfo method)
    {
        var schema = JsonNode.Parse(tool.InputSchema.GetRawText())!;
        foreach (var parameter in method.GetParameters())
        {
            if (method.DeclaringType == typeof(Command.CommandTools) && method.Name == nameof(Command.CommandTools.InsertRow) && parameter.Name == "row")
                schema["properties"]!["row"]!["type"] = "object";
            if (parameter.GetCustomAttribute<ToolChoicesDescriptionAttribute>() is not { } choices) continue;
            var property = schema["properties"]?[parameter.Name!]
                ?? throw new InvalidOperationException($"Schema missing {parameter.Name}.");
            var target = parameter.ParameterType.IsArray ? property["items"]! : property;
            target["enum"] = JsonSerializer.SerializeToNode(choices.Choices);
        }
        tool.InputSchema = JsonSerializer.SerializeToElement(schema);
        return tool;
    }
}
