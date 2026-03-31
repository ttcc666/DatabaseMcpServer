using ClosedXML.Excel;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Tools;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using SqlSugar;
using System.ComponentModel;
using System.Data;
using System.Text.Json;
using DbType = SqlSugar.DbType;

namespace DatabaseMcpServer.Tools.Export;

/// <summary>
/// Excel 导出工具类，支持 SQL 查询结果和表数据导出。
/// </summary>
[McpServerToolType]
internal class ExcelExportTools : McpToolBase
{
    public ExcelExportTools(
        IDatabaseConfigService databaseConfig,
        IDatabaseHelperService databaseHelper,
        IJsonResultSerializer resultSerializer,
        ILogger<ExcelExportTools> logger)
        : base(databaseConfig, databaseHelper, resultSerializer, logger)
    {
    }

    [McpServerTool]
    [Description("Export SQL query results to Excel file with formatting options")]
    public string ExportQueryToExcel(
        [Description("SQL query to execute and export")] string sql,
        [Description("Output Excel file path (optional, will generate temp file if not provided)")] string? filePath = null,
        [Description("Optional JSON parameters for parameterized queries")] string? parameters = null,
        [Description("Worksheet name (default: 'Data')")] string worksheetName = "Data",
        [Description("Include auto filters (default: true)")] bool autoFilter = true,
        [Description("Auto size columns (default: true)")] bool autoSizeColumns = true,
        [Description("Freeze header row (default: true)")] bool freezeHeader = true,
        [Description("Return format: 'base64' or 'path' (default: 'base64')")] string returnFormat = "base64",
        [Description("Maximum rows per batch for performance (default: 10000)")] int batchSize = 10000)
    {
        return Execute(() =>
        {
            var startTime = DateTime.UtcNow;
            ValidateExportParameters(sql, filePath, batchSize);

            var actualFilePath = string.IsNullOrWhiteSpace(filePath)
                ? GenerateTempFilePath(worksheetName)
                : filePath;

            using var db = DatabaseConfig.CreateClient();
            var parsedParams = DatabaseHelper.ParseParameters(parameters);
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add(SanitizeWorksheetName(worksheetName));

            Logger.LogInformation("开始导出 SQL 查询结果到 Excel: {FilePath}", actualFilePath);

            var totalRows = PopulateWorksheetFromQuery(worksheet, db, sql, parsedParams, batchSize);
            if (totalRows == 0)
            {
                return new
                {
                    success = false,
                    message = "查询结果为空，无法导出",
                    sql = sql.TruncateForLog()
                };
            }

            ApplyWorksheetFormatting(worksheet, autoFilter, autoSizeColumns, freezeHeader);
            workbook.SaveAs(actualFilePath);

            var processingTime = DateTime.UtcNow - startTime;
            var response = GenerateExportResponse(actualFilePath, returnFormat.ToLowerInvariant(), totalRows, worksheetName, processingTime);

            if (string.IsNullOrWhiteSpace(filePath))
            {
                ScheduleFileCleanup(actualFilePath);
            }

            Logger.LogInformation("Excel 导出完成: {TotalRows} 行, 耗时 {ElapsedMs}ms", totalRows, processingTime.TotalMilliseconds);
            return response;
        });
    }

    [McpServerTool]
    [Description("Export entire table data to Excel file with schema information")]
    public string ExportTableToExcel(
        [Description("Table name to export")] string tableName,
        [Description("Output Excel file path (optional, will generate temp file if not provided)")] string? filePath = null,
        [Description("WHERE clause for filtering (optional)")] string? whereClause = null,
        [Description("Include schema worksheet (default: true)")] bool includeSchema = true,
        [Description("Worksheet name prefix (default: table name)")] string worksheetPrefix = "",
        [Description("Return format: 'base64' or 'path' (default: 'base64')")] string returnFormat = "base64",
        [Description("Maximum rows per batch for performance (default: 10000)")] int batchSize = 10000)
    {
        return Execute(() =>
        {
            var startTime = DateTime.UtcNow;
            ValidateTableExportParameters(tableName, whereClause, filePath, batchSize);

            var actualFilePath = string.IsNullOrWhiteSpace(filePath)
                ? GenerateTempFilePath($"{worksheetPrefix}{tableName}")
                : filePath;

            using var db = DatabaseConfig.CreateClient();
            ValidateTableExists(db, tableName);

            var worksheetName = string.IsNullOrWhiteSpace(worksheetPrefix) ? tableName : $"{worksheetPrefix}_{tableName}";
            using var workbook = new XLWorkbook();

            Logger.LogInformation("开始导出表 {TableName} 到 Excel: {FilePath}", tableName, actualFilePath);

            var sql = BuildTableExportSql(db.CurrentConnectionConfig.DbType, tableName, whereClause);
            var dataSheet = workbook.Worksheets.Add(SanitizeWorksheetName(worksheetName));
            var totalRows = PopulateWorksheetFromQuery(dataSheet, db, sql, null, batchSize);

            if (totalRows == 0)
            {
                return new
                {
                    success = false,
                    message = $"表 {tableName} 中没有数据",
                    tableName
                };
            }

            ApplyWorksheetFormatting(dataSheet, autoFilter: true, autoSizeColumns: true, freezeHeader: true);

            if (includeSchema)
            {
                AddSchemaWorksheet(workbook, tableName, db);
            }

            workbook.SaveAs(actualFilePath);

            var processingTime = DateTime.UtcNow - startTime;
            var response = GenerateExportResponse(actualFilePath, returnFormat.ToLowerInvariant(), totalRows, worksheetName, processingTime);

            if (string.IsNullOrWhiteSpace(filePath))
            {
                ScheduleFileCleanup(actualFilePath);
            }

            Logger.LogInformation("表导出完成: {TableName}, {TotalRows} 行, 耗时 {ElapsedMs}ms", tableName, totalRows, processingTime.TotalMilliseconds);
            return response;
        });
    }

    [McpServerTool]
    [Description("Export multiple SQL queries to separate worksheets in the same Excel file")]
    public string ExportMultipleQueriesToExcel(
        [Description("Dictionary of query names and SQL statements")] string queriesJson,
        [Description("Output Excel file path (optional, will generate temp file if not provided)")] string? filePath = null,
        [Description("Include summary worksheet (default: true)")] bool includeSummary = true,
        [Description("Return format: 'base64' or 'path' (default: 'base64')")] string returnFormat = "base64",
        [Description("Maximum rows per batch for performance (default: 10000)")] int batchSize = 10000)
    {
        return Execute(() =>
        {
            var startTime = DateTime.UtcNow;
            ValidateMultipleExportParameters(queriesJson, filePath, batchSize);

            var queries = ParseAndValidateQueries(queriesJson);
            var actualFilePath = string.IsNullOrWhiteSpace(filePath)
                ? GenerateTempFilePath("MultiQueryExport")
                : filePath;

            using var db = DatabaseConfig.CreateClient();
            using var workbook = new XLWorkbook();
            var exportResults = new List<QueryExportResult>();

            foreach (var (queryName, sql) in queries)
            {
                Logger.LogInformation("导出查询 {QueryName}: {SqlSample}", queryName, sql.TruncateForLog());

                try
                {
                    var worksheet = workbook.Worksheets.Add(SanitizeWorksheetName(queryName));
                    var rowCount = PopulateWorksheetFromQuery(worksheet, db, sql, null, batchSize);
                    ApplyWorksheetFormatting(worksheet, autoFilter: true, autoSizeColumns: true, freezeHeader: true);

                    exportResults.Add(new QueryExportResult
                    {
                        QueryName = queryName,
                        RowCount = rowCount,
                        Success = true
                    });
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "导出查询 {QueryName} 失败", queryName);
                    exportResults.Add(new QueryExportResult
                    {
                        QueryName = queryName,
                        RowCount = 0,
                        Success = false,
                        Error = ex.Message
                    });
                }
            }

            if (includeSummary)
            {
                AddSummaryWorksheet(workbook, exportResults);
            }

            workbook.SaveAs(actualFilePath);

            var processingTime = DateTime.UtcNow - startTime;
            var totalRows = exportResults.Sum(result => result.RowCount);
            var response = GenerateExportResponse(actualFilePath, returnFormat.ToLowerInvariant(), totalRows, "MultipleQueries", processingTime);

            if (string.IsNullOrWhiteSpace(filePath))
            {
                ScheduleFileCleanup(actualFilePath);
            }

            Logger.LogInformation(
                "多查询导出完成: {QueryCount} 个查询, {TotalRows} 行, 耗时 {ElapsedMs}ms",
                queries.Count,
                totalRows,
                processingTime.TotalMilliseconds);

            return response;
        });
    }

    private void ValidateExportParameters(string sql, string? filePath, int batchSize)
    {
        SqlSafetyGuard.EnsureReadOnlySql(sql, DatabaseHelper);

        if (batchSize <= 0 || batchSize > 50000)
        {
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "批量大小必须在 1-50000 之间");
        }

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            EnsureDirectoryExists(filePath);
        }
    }

    private void ValidateTableExportParameters(string tableName, string? whereClause, string? filePath, int batchSize)
    {
        SqlSafetyGuard.EnsureSafeTableName(tableName);
        SqlSafetyGuard.EnsureSafeWhereClause(whereClause);

        if (batchSize <= 0 || batchSize > 50000)
        {
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "批量大小必须在 1-50000 之间");
        }

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            EnsureDirectoryExists(filePath);
        }
    }

    private void ValidateMultipleExportParameters(string queriesJson, string? filePath, int batchSize)
    {
        if (string.IsNullOrWhiteSpace(queriesJson))
        {
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "查询 JSON 不能为空");
        }

        if (batchSize <= 0 || batchSize > 50000)
        {
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "批量大小必须在 1-50000 之间");
        }

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            EnsureDirectoryExists(filePath);
        }
    }

    private Dictionary<string, string> ParseAndValidateQueries(string queriesJson)
    {
        try
        {
            var queries = JsonSerializer.Deserialize<Dictionary<string, string>>(queriesJson);
            if (queries == null || queries.Count == 0)
            {
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "查询列表不能为空");
            }

            foreach (var (queryName, sql) in queries)
            {
                if (string.IsNullOrWhiteSpace(queryName))
                {
                    throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "查询名称不能为空");
                }

                if (string.IsNullOrWhiteSpace(sql))
                {
                    throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, $"查询 '{queryName}' 的 SQL 不能为空");
                }

                SqlSafetyGuard.EnsureReadOnlySql(sql, DatabaseHelper);
            }

            return queries;
        }
        catch (JsonException ex)
        {
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "queriesJson 不是有效的 JSON 对象", ex);
        }
    }

    private int PopulateWorksheetFromQuery(
        IXLWorksheet worksheet,
        ISqlSugarClient db,
        string sql,
        SugarParameter[]? parameters,
        int batchSize)
    {
        using var reader = GetQueryReader(db, sql, parameters);
        return PopulateWorksheetFromDataReader(worksheet, reader, batchSize);
    }

    private IDataReader GetQueryReader(ISqlSugarClient db, string sql, SugarParameter[]? parameters)
    {
        try
        {
            if (parameters != null && parameters.Length > 0)
            {
                return db.Ado.GetDataReader(sql, parameters);
            }

            return db.Ado.GetDataReader(sql, (object?)null);
        }
        catch (Exception ex)
        {
            throw new DatabaseMcpException(DatabaseErrorCode.QueryExecutionFailed, $"查询执行失败: {ex.Message}", ex);
        }
    }

    private int PopulateWorksheetFromDataReader(IXLWorksheet worksheet, IDataReader reader, int batchSize)
    {
        var fieldCount = reader.FieldCount;
        if (fieldCount == 0)
        {
            return 0;
        }

        for (var col = 0; col < fieldCount; col++)
        {
            var cell = worksheet.Cell(1, col + 1);
            cell.Value = reader.GetName(col);
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        }

        var rowIndex = 2;
        var rowCount = 0;

        while (reader.Read())
        {
            for (var col = 0; col < fieldCount; col++)
            {
                var cell = worksheet.Cell(rowIndex, col + 1);
                cell.Value = reader.IsDBNull(col) ? string.Empty : ConvertToXLCellValue(reader.GetValue(col));
            }

            rowIndex++;
            rowCount++;

            if (rowCount % batchSize == 0)
            {
                Logger.LogDebug("工作表 {Worksheet} 已写入 {RowCount} 行", worksheet.Name, rowCount);
            }
        }

        return rowCount;
    }

    private static XLCellValue ConvertToXLCellValue(object value)
    {
        return value switch
        {
            bool booleanValue => booleanValue,
            DateTime dateTime => dateTime,
            DateTimeOffset dateTimeOffset => dateTimeOffset.DateTime,
            TimeSpan timeSpan => timeSpan.ToString(),
            string stringValue => stringValue,
            char charValue => charValue.ToString(),
            Guid guid => guid.ToString(),
            byte number => number.ToString(),
            sbyte number => number.ToString(),
            short number => number.ToString(),
            ushort number => number.ToString(),
            int number => number.ToString(),
            uint number => number.ToString(),
            long number => number.ToString(),
            ulong number => number.ToString(),
            float number => number.ToString("F"),
            double number => number.ToString("G"),
            decimal number => number.ToString(),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static void ApplyWorksheetFormatting(IXLWorksheet worksheet, bool autoFilter, bool autoSizeColumns, bool freezeHeader)
    {
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;
        var lastCol = worksheet.LastColumnUsed()?.ColumnNumber() ?? 1;
        var range = worksheet.Range(1, 1, lastRow, lastCol);

        if (autoFilter && lastRow > 1)
        {
            range.SetAutoFilter();
        }

        if (autoSizeColumns)
        {
            worksheet.Columns().AdjustToContents();
        }

        if (freezeHeader && lastRow > 1)
        {
            worksheet.SheetView.FreezeRows(1);
        }

        if (lastRow > 1)
        {
            var dataRange = worksheet.Range(2, 1, lastRow, lastCol);
            dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        }
    }

    private static string GenerateTempFilePath(string suggestedName)
    {
        var tempDir = Path.GetTempPath();
        var fileName = string.IsNullOrWhiteSpace(suggestedName)
            ? $"Export_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
            : $"{SanitizeFileName(suggestedName)}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

        return Path.Combine(tempDir, $"DatabaseMCP_{fileName}");
    }

    private static object GenerateExportResponse(
        string filePath,
        string returnFormat,
        int totalRows,
        string sheetName,
        TimeSpan processingTime)
    {
        if (returnFormat == "path")
        {
            return new
            {
                success = true,
                exportPath = filePath,
                totalRows,
                worksheetName = sheetName,
                processingTimeMs = processingTime.TotalMilliseconds,
                fileSize = new FileInfo(filePath).Length
            };
        }

        var bytes = File.ReadAllBytes(filePath);
        return new
        {
            success = true,
            fileName = Path.GetFileName(filePath),
            base64Content = Convert.ToBase64String(bytes),
            totalRows,
            worksheetName = sheetName,
            processingTimeMs = processingTime.TotalMilliseconds,
            fileSize = bytes.Length
        };
    }

    private void ScheduleFileCleanup(string filePath)
    {
        Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromHours(24));
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    Logger.LogDebug("临时文件已自动清理: {FilePath}", filePath);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("清理临时文件失败: {FilePath}, Error: {Error}", filePath, ex.Message);
            }
        });
    }

    private static void EnsureDirectoryExists(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static string SanitizeWorksheetName(string name)
    {
        var invalidChars = new[] { '\\', '/', '*', '[', ']', ':', '?' };
        foreach (var invalidChar in invalidChars)
        {
            name = name.Replace(invalidChar.ToString(), "_", StringComparison.Ordinal);
        }

        return name.Length > 31 ? name[..31] : name;
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalidChar.ToString(), "_", StringComparison.Ordinal);
        }

        return name;
    }

    private static string BuildTableExportSql(DbType dbType, string tableName, string? whereClause)
    {
        var quotedTableName = SqlSafetyGuard.QuoteTableName(tableName, dbType);
        var sql = $"SELECT * FROM {quotedTableName}";

        if (!string.IsNullOrWhiteSpace(whereClause))
        {
            var normalizedWhereClause = whereClause.Trim();
            sql += normalizedWhereClause.StartsWith("WHERE", StringComparison.OrdinalIgnoreCase)
                ? $" {normalizedWhereClause}"
                : $" WHERE {normalizedWhereClause}";
        }

        return sql;
    }

    private static void ValidateTableExists(ISqlSugarClient db, string tableName)
    {
        var simpleTableName = tableName
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault() ?? tableName;

        var tableInfos = db.DbMaintenance.GetTableInfoList(false) ?? [];
        var exists = tableInfos.Any(table =>
            string.Equals(table.Name, tableName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(table.Name, simpleTableName, StringComparison.OrdinalIgnoreCase));

        if (!exists)
        {
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, $"表 '{tableName}' 不存在，请先检查表名是否正确。");
        }
    }

    private void AddSchemaWorksheet(XLWorkbook workbook, string tableName, ISqlSugarClient db)
    {
        try
        {
            var schemaSheet = workbook.Worksheets.Add("Schema");
            var columns = db.DbMaintenance.GetColumnInfosByTableName(tableName);

            schemaSheet.Cell(1, 1).Value = "ColumnName";
            schemaSheet.Cell(1, 2).Value = "DataType";
            schemaSheet.Cell(1, 3).Value = "Length";
            schemaSheet.Cell(1, 4).Value = "IsNullable";
            schemaSheet.Cell(1, 5).Value = "DefaultValue";
            schemaSheet.Cell(1, 6).Value = "IsPrimaryKey";
            schemaSheet.Cell(1, 7).Value = "IsIdentity";

            schemaSheet.Range(1, 1, 1, 7).Style.Font.Bold = true;
            schemaSheet.Range(1, 1, 1, 7).Style.Fill.BackgroundColor = XLColor.LightGray;

            var row = 2;
            foreach (var column in columns)
            {
                schemaSheet.Cell(row, 1).Value = column.DbColumnName;
                schemaSheet.Cell(row, 2).Value = column.DataType;
                schemaSheet.Cell(row, 3).Value = column.Length > 0 ? column.Length.ToString() : "-";
                schemaSheet.Cell(row, 4).Value = column.IsNullable ? "Yes" : "No";
                schemaSheet.Cell(row, 5).Value = column.DefaultValue ?? "-";
                schemaSheet.Cell(row, 6).Value = column.IsPrimarykey ? "Yes" : "No";
                schemaSheet.Cell(row, 7).Value = column.IsIdentity ? "Yes" : "No";
                row++;
            }

            schemaSheet.Columns().AdjustToContents();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "无法获取表 {TableName} 的模式信息", tableName);
        }
    }

    private static void AddSummaryWorksheet(XLWorkbook workbook, IReadOnlyCollection<QueryExportResult> exportResults)
    {
        var summarySheet = workbook.Worksheets.Add("Summary");
        summarySheet.Cell(1, 1).Value = "Query Name";
        summarySheet.Cell(1, 2).Value = "Row Count";
        summarySheet.Cell(1, 3).Value = "Status";
        summarySheet.Cell(1, 4).Value = "Error";

        summarySheet.Range(1, 1, 1, 4).Style.Font.Bold = true;
        summarySheet.Range(1, 1, 1, 4).Style.Fill.BackgroundColor = XLColor.LightGray;

        var row = 2;
        foreach (var result in exportResults)
        {
            summarySheet.Cell(row, 1).Value = result.QueryName;
            summarySheet.Cell(row, 2).Value = result.RowCount;
            summarySheet.Cell(row, 3).Value = result.Success ? "Success" : "Failed";
            summarySheet.Cell(row, 4).Value = result.Error ?? "-";
            summarySheet.Cell(row, 3).Style.Font.FontColor = result.Success ? XLColor.Green : XLColor.Red;
            row++;
        }

        summarySheet.Columns().AdjustToContents();
    }

    private sealed class QueryExportResult
    {
        public string QueryName { get; set; } = string.Empty;

        public int RowCount { get; set; }

        public bool Success { get; set; }

        public string? Error { get; set; }
    }
}

public static class StringExtensions
{
    public static string TruncateForLog(this string? value, int maxLength = 100)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length > maxLength ? $"{value[..maxLength]}..." : value;
    }
}
