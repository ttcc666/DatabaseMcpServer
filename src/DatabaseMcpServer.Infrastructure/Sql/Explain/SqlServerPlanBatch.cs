using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DatabaseMcpServer.Models;
using Microsoft.Data.SqlClient;

namespace DatabaseMcpServer.Helpers;

internal static class SqlServerPlanBatch
{
    public static void BindTypes(DbCommand command)
    {
        // SHOWPLAN does not execute the sp_executesql RPC used for bound parameters.
        // Declare typed locals instead; values are never interpolated or evaluated.
        var declarations = new StringBuilder();
        foreach (SqlParameter parameter in command.Parameters)
        {
            var name = parameter.ParameterName.TrimStart('@');
            if (!Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$"))
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "执行计划参数名无效。");
            var type = parameter.SqlDbType switch
            {
                SqlDbType.NVarChar => $"nvarchar({Size(parameter, 4000)})",
                SqlDbType.VarChar => $"varchar({Size(parameter, 8000)})",
                SqlDbType.VarBinary => $"varbinary({Size(parameter, 8000)})",
                SqlDbType.Decimal => $"decimal(38,{DecimalScale(parameter).ToString(CultureInfo.InvariantCulture)})",
                SqlDbType.BigInt => "bigint", SqlDbType.Int => "int", SqlDbType.SmallInt => "smallint",
                SqlDbType.TinyInt => "tinyint", SqlDbType.Bit => "bit", SqlDbType.UniqueIdentifier => "uniqueidentifier",
                SqlDbType.Date => "date", SqlDbType.DateTime => "datetime", SqlDbType.DateTime2 => "datetime2",
                SqlDbType.DateTimeOffset => "datetimeoffset", SqlDbType.Time => "time",
                SqlDbType.Float => "float", SqlDbType.Real => "real",
                _ => throw new DatabaseMcpException(DatabaseErrorCode.UnsupportedCapability, "执行计划不支持此 SQL Server 参数类型。")
            };
            declarations.Append("DECLARE @").Append(name).Append(' ').Append(type).AppendLine(";");
        }
        command.CommandText = declarations + command.CommandText;
        command.Parameters.Clear();
    }

    private static string Size(SqlParameter parameter, int maximum)
        => parameter.Size < 0 || parameter.Size > maximum ? "max" : Math.Max(1, parameter.Size).ToString(CultureInfo.InvariantCulture);

    private static int DecimalScale(SqlParameter parameter)
        => parameter.Scale != 0 ? parameter.Scale : parameter.Value is decimal value ? (decimal.GetBits(value)[3] >> 16) & 0xff : 0;
}
