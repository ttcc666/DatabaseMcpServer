using System.Globalization;
using System.Text;
using System.Text.Json;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using Microsoft.Extensions.Logging;

namespace DatabaseMcpServer.Services;

internal sealed class SqlExportService(IDatabaseConfigService config, IDatabaseHelperService helper,
    ILogger<SqlExportService> logger, DatabaseExecutionOptions options, DatabaseMetadataCache cache, SqlDatabaseSessions sessions)
    : SqlOperationExecutor(config, helper, logger, options, cache, sessions)
{
    public Task<object> Export(Stream destination, string sql, DatabaseParameter[]? parameters, string format, string? databaseName,
        int maxRows, int maxFileMiB, int? commandTimeoutSeconds, CancellationToken cancellationToken)
        => WithDeadlineAsync(async (context, token) =>
        {
            SqlSafetyGuard.EnsureReadOnlySql(sql, DatabaseHelper);
            if (format is not ("csv" or "jsonl") || maxFileMiB is < 1 or > 1024)
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "format 必须为 csv/jsonl；max-file-mib 必须在 1-1024 之间。");
            ExecutionOptions.ResolveQuery(maxRows, null, commandTimeoutSeconds, bounded: true);
            var parametersBound = SqlParameterBinder.Bind(parameters, context.Client.CurrentConnectionConfig.DbType);
            await using var readerLease = await SqlSugarReaderAdapter.OpenAsync(context.Client, sql, parametersBound, token);
            var reader = readerLease.Reader;
            var names = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
            if (names.Any(string.IsNullOrEmpty) || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "导出列必须有唯一名称，请在 SQL 中指定别名。");
            using var encoder = new QueryRowEncoder(reader, names);
            var values = format == "csv" ? new Dictionary<string, object?>() : null;
            var bytes = 0L;
            var rows = 0;
            async Task WriteLine(string line)
            {
                await WriteEncodedLine(Encoding.UTF8.GetBytes(line));
            }
            async Task WriteEncodedLine(byte[] encoded)
            {
                if (bytes + encoded.Length + 1 > (long)maxFileMiB * 1024 * 1024)
                    throw new DatabaseMcpException(DatabaseErrorCode.ResponseLimitExceeded, "导出文件超过 max-file-mib，未发布部分文件。");
                await destination.WriteAsync(encoded, token);
                await destination.WriteAsync(Newline, token);
                bytes += encoded.Length + 1;
            }
            if (format == "csv") await WriteLine(string.Join(',', names.Select(Csv)));
            while (rows < maxRows && await QueryReader.ReadAsync(reader, token))
            {
                var encoded = await encoder.ReadAsync(ExecutionOptions.MaxResponseBytes, token, values);
                if (format == "jsonl") await WriteEncodedLine(encoded.Utf8Json);
                else await WriteLine(string.Join(',', names.Select(name => Csv(FormatValue(values![name])))));
                rows++;
            }
            if (rows < maxRows) readerLease.Complete();
            await destination.FlushAsync(token);
            return new { success = true, format, rowCount = rows, bytesWritten = bytes, limitReached = rows == maxRows,
                truncation = rows == maxRows ? "unknown; stopped without reading another row" : "none" };
        }, commandTimeoutSeconds, cancellationToken, databaseName);

    private static readonly byte[] Newline = [(byte)'\n'];

    private static string FormatValue(object? value) => value switch
    {
        null => "", byte[] bytes => Convert.ToBase64String(bytes), DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset date => date.ToString("O", CultureInfo.InvariantCulture),
        IFormattable formatted => formatted.ToString(null, CultureInfo.InvariantCulture), _ => value.ToString() ?? ""
    };
    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
