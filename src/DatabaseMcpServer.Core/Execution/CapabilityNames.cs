using System.Text.Json;
using DatabaseMcpServer.Models;

namespace DatabaseMcpServer.Helpers;

internal static class CapabilityNames<T> where T : struct, Enum
{
    private static readonly IReadOnlyDictionary<string, T> Values = Enum.GetValues<T>()
        .ToDictionary(value => JsonNamingPolicy.CamelCase.ConvertName(value.ToString()), StringComparer.Ordinal);

    public static IEnumerable<string> All => Values.Keys;
    public static string Name(T value) => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
    public static T Parse(string name) => name != null && Values.TryGetValue(name, out var value) ? value
        : throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, $"无效 {typeof(T).Name}；允许值：{string.Join(", ", All)}。");
}
