using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DatabaseMcpServer.Models;
using SqlSugar;
using AdoType = System.Data.DbType;

namespace DatabaseMcpServer.Helpers;

internal static class SqlParameterBinder
{
    public static SugarParameter[] Bind(IReadOnlyList<DatabaseParameter>? parameters,
        SqlSugar.DbType provider, bool storedProcedure = false)
    {
        if (parameters == null) return [];

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return parameters.Select(parameter => BindOne(parameter, names, provider, storedProcedure)).ToArray();
    }

    private static SugarParameter BindOne(DatabaseParameter parameter, HashSet<string> names,
        SqlSugar.DbType provider, bool storedProcedure)
    {
        if (parameter == null || string.IsNullOrWhiteSpace(parameter.Name) ||
            !Regex.IsMatch(parameter.Name, @"^[@:$]?[A-Za-z_][A-Za-z0-9_]*$") ||
            !names.Add(parameter.Name.TrimStart('@', ':', '$')))
            throw Invalid("参数名无效或重复。");
        if (!Enum.IsDefined(parameter.Direction) || parameter.DbType is { } type && !Enum.IsDefined(type))
            throw Invalid("参数类型或方向无效。");
        if (parameter.Direction != DatabaseParameterDirection.Input &&
            (!storedProcedure || provider is not (SqlSugar.DbType.SqlServer or SqlSugar.DbType.MySql)))
            throw new DatabaseMcpException(DatabaseErrorCode.UnsupportedCapability, "类型化输出参数目前支持 SQL Server/MySQL 存储过程。");
        if (parameter.Direction == DatabaseParameterDirection.ReturnValue && provider != SqlSugar.DbType.SqlServer)
            throw new DatabaseMcpException(DatabaseErrorCode.UnsupportedCapability, "ReturnValue 目前仅支持 SQL Server。");
        if (parameter.DbType == DatabaseParameterType.DateTimeOffset && provider is not (SqlSugar.DbType.SqlServer or SqlSugar.DbType.PostgreSQL))
            throw new DatabaseMcpException(DatabaseErrorCode.UnsupportedCapability, "DateTimeOffset 目前仅支持 SQL Server/PostgreSQL；不要静默丢失时区。");
        var isNull = parameter.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;
        if ((isNull || parameter.Direction != DatabaseParameterDirection.Input) && parameter.DbType == null)
            throw Invalid("NULL 和输出参数必须显式指定 dbType。");
        if (parameter.Size is <= 0 || parameter.Precision == 0 ||
            parameter.Scale.HasValue && (!parameter.Precision.HasValue || parameter.Scale > parameter.Precision))
            throw Invalid("size/precision/scale 无效。");
        if ((parameter.Precision.HasValue || parameter.Scale.HasValue) && parameter.DbType != DatabaseParameterType.Decimal)
            throw Invalid("precision/scale 只用于 Decimal。");
        if (parameter.Precision.HasValue && !parameter.Scale.HasValue)
            throw Invalid("声明 precision 时必须同时声明 scale，避免驱动默认舍入。");
        if (parameter.Precision.HasValue && parameter.Direction != DatabaseParameterDirection.Input)
            throw new DatabaseMcpException(DatabaseErrorCode.UnsupportedCapability,
                "当前 SqlSugar 不传递 Precision；precision 仅支持输入值校验，不能用于输出参数。");
        if (parameter.Direction != DatabaseParameterDirection.Input &&
            parameter.DbType is DatabaseParameterType.String or DatabaseParameterType.AnsiString or DatabaseParameterType.Binary && parameter.Size == null)
            throw Invalid("字符串/二进制输出参数必须指定 size。");

        object? value;
        try
        {
            if (parameter.Value.ValueKind == JsonValueKind.Array)
            {
                if (parameter.Direction != DatabaseParameterDirection.Input || isNull)
                    throw Invalid("数组仅支持输入参数。");
                if (parameter.DbType != null) throw Invalid("类型化数组暂不支持 dbType；请使用 JSON 值数组绑定 IN 参数。");
                value = JsonElementValueConverter.ConvertToValue(parameter.Value);
            }
            else value = isNull ? null : ConvertValue(parameter.Value, parameter.DbType);
            if (value is DateTimeOffset offset && provider == SqlSugar.DbType.PostgreSQL)
                value = offset.ToUniversalTime(); // Npgsql timestamp with time zone requires UTC; preserve the instant.
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or InvalidOperationException)
        {
            throw Invalid($"参数 '{parameter.Name}' 与声明的 dbType 不匹配。");
        }

        if (value is string text && parameter.Size is { } size && text.Length > size ||
            value is byte[] bytes && parameter.Size is { } binarySize && bytes.Length > binarySize)
            throw Invalid("输入长度超过参数 size，已拒绝截断。");
        if (value is decimal amount && parameter.DbType == DatabaseParameterType.Decimal)
        {
            if (parameter.Scale is { } scale && (scale > 28 || decimal.Round(amount, scale) != amount))
                throw Invalid("Decimal 参数超过声明的 scale，已拒绝舍入。");
            if (parameter.Precision is { } precision)
            {
                var integerDigits = decimal.Truncate(amount).ToString("0", CultureInfo.InvariantCulture).TrimStart('-', '0').Length;
                if (integerDigits > precision - (parameter.Scale ?? 0)) throw Invalid("Decimal 参数超过声明的 precision。");
            }
        }
        var result = new SugarParameter(parameter.Name, value)
        {
            Direction = Enum.Parse<ParameterDirection>(parameter.Direction.ToString())
        };
        if (parameter.DbType.HasValue) result.DbType = Enum.Parse<AdoType>(parameter.DbType.Value.ToString());
        if (parameter.Size.HasValue) result.Size = parameter.Size.Value;
        if (parameter.Scale.HasValue) result.Scale = parameter.Scale.Value;
        return result;
    }

    private static object? ConvertValue(JsonElement value, DatabaseParameterType? type)
    {
        var text = value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
        return type switch
        {
            null => JsonElementValueConverter.ConvertToValue(value),
            DatabaseParameterType.String or DatabaseParameterType.AnsiString => value.GetString(),
            DatabaseParameterType.Int16 => short.Parse(text, CultureInfo.InvariantCulture),
            DatabaseParameterType.Int32 => int.Parse(text, CultureInfo.InvariantCulture),
            DatabaseParameterType.Int64 => long.Parse(text, CultureInfo.InvariantCulture),
            DatabaseParameterType.Byte => byte.Parse(text, CultureInfo.InvariantCulture),
            DatabaseParameterType.Boolean => bool.Parse(text),
            DatabaseParameterType.Guid => Guid.Parse(text),
            DatabaseParameterType.Binary => Convert.FromBase64String(value.GetString()!),
            DatabaseParameterType.Date => DateTime.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            DatabaseParameterType.DateTime or DatabaseParameterType.DateTime2 => DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            DatabaseParameterType.DateTimeOffset => ParseOffset(text),
            DatabaseParameterType.Time => TimeSpan.Parse(text, CultureInfo.InvariantCulture),
            _ => ConvertNumber(text, type.Value)
        };
    }

    private static DateTimeOffset ParseOffset(string text)
    {
        if (!Regex.IsMatch(text, @"T.*(?:Z|[+-]\d{2}:\d{2})$"))
            throw new FormatException("DateTimeOffset requires an explicit ISO 8601 offset.");
        return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private static object ConvertNumber(string text, DatabaseParameterType type)
    {
        using var parsed = JsonDocument.Parse(text);
        var value = JsonElementValueConverter.ConvertToValue(parsed.RootElement);
        object result = type switch
        {
            DatabaseParameterType.Decimal => (object)Convert.ToDecimal(value, CultureInfo.InvariantCulture),
            DatabaseParameterType.Double => (object)Convert.ToDouble(value, CultureInfo.InvariantCulture),
            DatabaseParameterType.Single => (object)Convert.ToSingle(value, CultureInfo.InvariantCulture),
            _ => throw Invalid("不支持的数值类型。")
        };
        if (result is double d && !double.IsFinite(d) || result is float f && !float.IsFinite(f) ||
            !JsonElement.DeepEquals(parsed.RootElement, JsonSerializer.SerializeToElement(result, result.GetType())))
            throw Invalid("显式数值类型无法保持参数值，已拒绝舍入。");
        return result;
    }

    private static DatabaseMcpException Invalid(string message) => new(DatabaseErrorCode.InvalidParameters, message);
}
