using ClosedXML.Excel;
using DatabaseMcpServer.Filters;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using SqlSugar;
using System.ComponentModel;
using System.Data;
using System.Text.Json;

namespace DatabaseMcpServer.Tools.Export;

/// <summary>
/// Excel 导出工具类，支持 SQL 查询结果和表数据导出
/// </summary>
public class ExcelExportTools
{
    private readonly IDatabaseConfigService _databaseConfig;
    private readonly IDatabaseHelperService _databaseHelper;
    private readonly ILogger<ExcelExportTools> _logger;

    public ExcelExportTools(IDatabaseConfigService databaseConfig,
                           IDatabaseHelperService databaseHelper,
                           ILogger<ExcelExportTools> logger)
    {
        _databaseConfig = databaseConfig;
        _databaseHelper = databaseHelper;
        _logger = logger;
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
        var startTime = DateTime.UtcNow;

        try
        {
            ValidateExportParameters(sql, filePath, batchSize);

            // 生成文件路径
            var actualFilePath = string.IsNullOrWhiteSpace(filePath)
                ? GenerateTempFilePath(worksheetName)
                : filePath;

            using var db = _databaseConfig.CreateClient();
            var parsedParams = _databaseHelper.ParseParameters(parameters);

            _logger?.LogInformation("开始导出 SQL 查询结果到 Excel: {FilePath}", actualFilePath);

            // 执行查询获取数据
            var dataTable = GetQueryData(db, sql, parsedParams);

            if (dataTable.Rows.Count == 0)
            {
                return _databaseHelper.SerializeResult(new
                {
                    success = false,
                    message = "查询结果为空，无法导出",
                    sql = sql.TruncateForLog()
                });
            }

            // 导出到 Excel
            ExportDataTableToExcel(dataTable, actualFilePath, worksheetName, autoFilter, autoSizeColumns, freezeHeader);

            var processingTime = DateTime.UtcNow - startTime;
            var totalRows = dataTable.Rows.Count;

            // 准备返回结果
            var result = GenerateExportResponse(actualFilePath, returnFormat.ToLower(), totalRows, worksheetName, processingTime);

            // 如果是临时文件，安排清理
            if (string.IsNullOrWhiteSpace(filePath))
            {
                ScheduleFileCleanup(actualFilePath);
            }

            _logger?.LogInformation("Excel 导出完成: {TotalRows} 行, 耗时 {ElapsedMs}ms", totalRows, processingTime.TotalMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "导出 SQL 查询到 Excel 失败");
            return McpExceptionFilter.HandleException(ex, _logger!);
        }
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
        var startTime = DateTime.UtcNow;

        try
        {
            ValidateTableExportParameters(tableName, filePath, batchSize);

            var actualFilePath = string.IsNullOrWhiteSpace(filePath)
                ? GenerateTempFilePath($"{worksheetPrefix}{tableName}")
                : filePath;

            using var db = _databaseConfig.CreateClient();
            var worksheetName = string.IsNullOrWhiteSpace(worksheetPrefix) ? tableName : $"{worksheetPrefix}_{tableName}";

            _logger?.LogInformation("开始导出表 {TableName} 到 Excel: {FilePath}", tableName, actualFilePath);

            // 构建查询语句
            var sql = BuildTableExportSql(tableName, whereClause);
            var dataTable = GetQueryData(db, sql, null);

            if (dataTable.Rows.Count == 0)
            {
                return _databaseHelper.SerializeResult(new
                {
                    success = false,
                    message = $"表 {tableName} 中没有数据",
                    tableName = tableName
                });
            }

            using var workbook = new XLWorkbook();

            // 添加数据工作表
            var dataSheet = workbook.Worksheets.Add(worksheetName);
            PopulateWorksheetFromDataTable(dataSheet, dataTable);
            ApplyWorksheetFormatting(dataSheet, autoFilter: true, autoSizeColumns: true, freezeHeader: true);

            // 可选：添加模式工作表
            if (includeSchema)
            {
                AddSchemaWorksheet(workbook, tableName, db);
            }

            workbook.SaveAs(actualFilePath);

            var processingTime = DateTime.UtcNow - startTime;
            var totalRows = dataTable.Rows.Count;

            var result = GenerateExportResponse(actualFilePath, returnFormat.ToLower(), totalRows, worksheetName, processingTime);

            if (string.IsNullOrWhiteSpace(filePath))
            {
                ScheduleFileCleanup(actualFilePath);
            }

            _logger?.LogInformation("表导出完成: {TableName}, {TotalRows} 行, 耗时 {ElapsedMs}ms", tableName, totalRows, processingTime.TotalMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "导出表 {TableName} 到 Excel 失败", tableName);
            return McpExceptionFilter.HandleException(ex, _logger!);
        }
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
        var startTime = DateTime.UtcNow;

        try
        {
            ValidateMultipleExportParameters(queriesJson, filePath, batchSize);

            var queries = JsonSerializer.Deserialize<Dictionary<string, string>>(queriesJson);
            if (queries == null || queries.Count == 0)
            {
                throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "查询列表不能为空");
            }

            var actualFilePath = string.IsNullOrWhiteSpace(filePath)
                ? GenerateTempFilePath("MultiQueryExport")
                : filePath;

            using var db = _databaseConfig.CreateClient();
            using var workbook = new XLWorkbook();

            var exportResults = new List<object>();

            foreach (var kvp in queries)
            {
                var queryName = kvp.Key;
                var sql = kvp.Value;

                _logger?.LogInformation("导出查询 {QueryName}: {SqlSample}", queryName, sql.TruncateForLog());

                try
                {
                    var dataTable = GetQueryData(db, sql, null);
                    var worksheet = workbook.Worksheets.Add(SanitizeWorksheetName(queryName));
                    PopulateWorksheetFromDataTable(worksheet, dataTable);
                    ApplyWorksheetFormatting(worksheet, autoFilter: true, autoSizeColumns: true, freezeHeader: true);

                    exportResults.Add(new
                    {
                        queryName = queryName,
                        rowCount = dataTable.Rows.Count,
                        success = true,
                        error = (string?)null
                    });
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "导出查询 {QueryName} 失败", queryName);
                    exportResults.Add(new
                    {
                        queryName = queryName,
                        rowCount = 0,
                        success = false,
                        error = ex.Message
                    });
                }
            }

            // 添加汇总工作表
            if (includeSummary)
            {
                AddSummaryWorksheet(workbook, exportResults);
            }

            workbook.SaveAs(actualFilePath);

            var processingTime = DateTime.UtcNow - startTime;
            var totalRows = exportResults.Sum(r => ((dynamic)r).rowCount);

            var result = GenerateExportResponse(actualFilePath, returnFormat.ToLower(), totalRows, "MultipleQueries", processingTime);

            if (string.IsNullOrWhiteSpace(filePath))
            {
                ScheduleFileCleanup(actualFilePath);
            }

            _logger?.LogInformation("多查询导出完成: {QueryCount} 个查询, {TotalRows} 行, 耗时 {ElapsedMs}ms",
                queries.Count, totalRows, processingTime.TotalMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "多查询导出到 Excel 失败");
            return McpExceptionFilter.HandleException(ex, _logger!);
        }
    }

    #region 私有方法

    private void ValidateExportParameters(string sql, string? filePath, int batchSize)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "SQL 查询不能为空");
        }

        if (_databaseHelper.DetectDangerousOperation(sql))
        {
            throw new DatabaseMcpException(DatabaseErrorCode.DangerousOperation,
                "检测到潜在危险操作，只允许 SELECT 查询导出");
        }

        if (batchSize <= 0 || batchSize > 50000)
        {
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters,
                "批量大小必须在 1-50000 之间");
        }

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            EnsureDirectoryExists(filePath);
        }
    }

    private void ValidateTableExportParameters(string tableName, string? filePath, int batchSize)
    {
        if (string.IsNullOrWhiteSpace(tableName))
        {
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters, "表名不能为空");
        }

        if (batchSize <= 0 || batchSize > 50000)
        {
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters,
                "批量大小必须在 1-50000 之间");
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
            throw new DatabaseMcpException(DatabaseErrorCode.InvalidParameters,
                "批量大小必须在 1-50000 之间");
        }

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            EnsureDirectoryExists(filePath);
        }
    }

    private DataTable GetQueryData(ISqlSugarClient db, string sql, SugarParameter[]? parameters)
    {
        try
        {
            return parameters != null
                ? db.Ado.GetDataTable(sql, parameters)
                : db.Ado.GetDataTable(sql);
        }
        catch (Exception ex)
        {
            throw new DatabaseMcpException(DatabaseErrorCode.QueryExecutionFailed,
                $"查询执行失败: {ex.Message}", ex);
        }
    }

    private void ExportDataTableToExcel(DataTable dataTable, string filePath, string worksheetName,
        bool autoFilter, bool autoSizeColumns, bool freezeHeader)
    {
        try
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add(SanitizeWorksheetName(worksheetName));

            PopulateWorksheetFromDataTable(worksheet, dataTable);
            ApplyWorksheetFormatting(worksheet, autoFilter, autoSizeColumns, freezeHeader);

            workbook.SaveAs(filePath);
        }
        catch (Exception ex)
        {
            throw new DatabaseMcpException(DatabaseErrorCode.ExcelExportFailed,
                $"Excel 文件生成失败: {ex.Message}", ex);
        }
    }

    private void PopulateWorksheetFromDataTable(IXLWorksheet worksheet, DataTable dataTable)
    {
        // 添加表头
        for (int col = 0; col < dataTable.Columns.Count; col++)
        {
            var cell = worksheet.Cell(1, col + 1);
            cell.Value = dataTable.Columns[col].ColumnName;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        }

        // 添加数据
        for (int row = 0; row < dataTable.Rows.Count; row++)
        {
            for (int col = 0; col < dataTable.Columns.Count; col++)
            {
                var value = dataTable.Rows[row][col];
                var cell = worksheet.Cell(row + 2, col + 1);

                if (value == DBNull.Value || value == null)
                {
                    cell.Value = "";
                }
                else
                {
                    // 根据数据类型设置单元格值
                    cell.Value = ConvertToXLCellValue(value);
                }
            }
        }
    }

    private XLCellValue ConvertToXLCellValue(object value)
    {
        return value switch
        {
            // 布尔类型 - 保持为布尔值
            bool b => b,

            // 日期时间类型 - 保持为日期格式
            DateTime dt => dt,
            DateTimeOffset dto => dto.DateTime,
            TimeSpan ts => ts.ToString(),

            // 字符串类型 - 保持为文本
            string s => s,
            char c => c.ToString(),

            // Guid - 转换为文本
            Guid g => g.ToString(),

            // 数值类型 - 全部转换为文本格式，避免科学计数法
            byte b => b.ToString(),
            sbyte sb => sb.ToString(),
            short s => s.ToString(),
            ushort us => us.ToString(),
            int i => i.ToString(),
            uint ui => ui.ToString(),
            long l => l.ToString(),
            ulong ul => ul.ToString(),
            float f => f.ToString("F"),
            double d => d.ToString("G"),
            decimal dec => dec.ToString(),

            // 其他类型转换为字符串
            _ => value.ToString() ?? ""
        };
    }

    private void ApplyWorksheetFormatting(IXLWorksheet worksheet, bool autoFilter, bool autoSizeColumns, bool freezeHeader)
    {
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;
        var lastCol = worksheet.LastColumnUsed()?.ColumnNumber() ?? 1;
        var range = worksheet.Range(1, 1, lastRow, lastCol);

        // 自动筛选
        if (autoFilter && lastRow > 1)
        {
            range.SetAutoFilter();
        }

        // 自动调整列宽
        if (autoSizeColumns)
        {
            worksheet.Columns().AdjustToContents();
        }

        // 冻结首行
        if (freezeHeader && lastRow > 1)
        {
            worksheet.SheetView.FreezeRows(1);
        }

        // 添加表格样式
        if (lastRow > 1)
        {
            var dataRange = worksheet.Range(2, 1, lastRow, lastCol);
            dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        }
    }

    private string GenerateTempFilePath(string suggestedName)
    {
        var tempDir = Path.GetTempPath();
        var fileName = string.IsNullOrWhiteSpace(suggestedName)
            ? $"Export_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
            : $"{SanitizeFileName(suggestedName)}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

        return Path.Combine(tempDir, $"DatabaseMCP_{fileName}");
    }

    private string GenerateExportResponse(string filePath, string returnFormat, int totalRows,
        string sheetName, TimeSpan processingTime)
    {
        if (returnFormat == "path")
        {
            return _databaseHelper.SerializeResult(new
            {
                success = true,
                exportPath = filePath,
                totalRows = totalRows,
                worksheetName = sheetName,
                processingTimeMs = processingTime.TotalMilliseconds,
                fileSize = new FileInfo(filePath).Length
            });
        }
        else // base64
        {
            var bytes = File.ReadAllBytes(filePath);
            var base64 = Convert.ToBase64String(bytes);

            return _databaseHelper.SerializeResult(new
            {
                success = true,
                fileName = Path.GetFileName(filePath),
                base64Content = base64,
                totalRows = totalRows,
                worksheetName = sheetName,
                processingTimeMs = processingTime.TotalMilliseconds,
                fileSize = bytes.Length
            });
        }
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
                    _logger?.LogDebug("临时文件已自动清理: {FilePath}", filePath);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning("清理临时文件失败: {FilePath}, Error: {Error}", filePath, ex.Message);
            }
        });
    }

    private void EnsureDirectoryExists(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private string SanitizeWorksheetName(string name)
    {
        // Excel 工作表名称限制
        var invalidChars = new[] { '\\', '/', '*', '[', ']', ':', '?' };
        foreach (var c in invalidChars)
        {
            name = name.Replace(c.ToString(), "_");
        }

        // 长度限制
        return name.Length > 31 ? name.Substring(0, 31) : name;
    }

    private string SanitizeFileName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        foreach (var c in invalidChars)
        {
            name = name.Replace(c.ToString(), "_");
        }

        return name;
    }

    private string BuildTableExportSql(string tableName, string? whereClause)
    {
        var sql = $"SELECT * FROM {tableName}";

        if (!string.IsNullOrWhiteSpace(whereClause))
        {
            if (!whereClause.TrimStart().ToUpper().StartsWith("WHERE"))
            {
                sql += $" WHERE {whereClause}";
            }
            else
            {
                sql += $" {whereClause}";
            }
        }

        return sql;
    }

    private void AddSchemaWorksheet(XLWorkbook workbook, string tableName, ISqlSugarClient db)
    {
        try
        {
            var schemaSheet = workbook.Worksheets.Add("Schema");

            // 获取表结构信息
            var columns = db.DbMaintenance.GetColumnInfosByTableName(tableName);

            // 表头
            schemaSheet.Cell(1, 1).Value = "ColumnName";
            schemaSheet.Cell(1, 2).Value = "DataType";
            schemaSheet.Cell(1, 3).Value = "Length";
            schemaSheet.Cell(1, 4).Value = "IsNullable";
            schemaSheet.Cell(1, 5).Value = "DefaultValue";
            schemaSheet.Cell(1, 6).Value = "IsPrimaryKey";
            schemaSheet.Cell(1, 7).Value = "IsIdentity";

            // 表头样式
            schemaSheet.Range(1, 1, 1, 7).Style.Font.Bold = true;
            schemaSheet.Range(1, 1, 1, 7).Style.Fill.BackgroundColor = XLColor.LightGray;

            // 填充数据
            int row = 2;
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
            _logger?.LogWarning(ex, "无法获取表 {TableName} 的模式信息", tableName);
        }
    }

    private void AddSummaryWorksheet(XLWorkbook workbook, List<object> exportResults)
    {
        var summarySheet = workbook.Worksheets.Add("Summary");

        // 表头
        summarySheet.Cell(1, 1).Value = "Query Name";
        summarySheet.Cell(1, 2).Value = "Row Count";
        summarySheet.Cell(1, 3).Value = "Status";
        summarySheet.Cell(1, 4).Value = "Error";

        // 表头样式
        summarySheet.Range(1, 1, 1, 4).Style.Font.Bold = true;
        summarySheet.Range(1, 1, 1, 4).Style.Fill.BackgroundColor = XLColor.LightGray;

        // 填充数据
        int row = 2;
        foreach (dynamic result in exportResults)
        {
            summarySheet.Cell(row, 1).Value = result.queryName;
            summarySheet.Cell(row, 2).Value = result.rowCount;
            summarySheet.Cell(row, 3).Value = result.success ? "Success" : "Failed";
            summarySheet.Cell(row, 4).Value = result.error ?? "-";

            // 根据状态设置颜色
            if (result.success)
            {
                summarySheet.Cell(row, 3).Style.Font.FontColor = XLColor.Green;
            }
            else
            {
                summarySheet.Cell(row, 3).Style.Font.FontColor = XLColor.Red;
            }

            row++;
        }

        summarySheet.Columns().AdjustToContents();
    }

    #endregion 私有方法
}

public static class StringExtensions
{
    public static string TruncateForLog(this string? str, int maxLength = 100)
    {
        if (string.IsNullOrWhiteSpace(str))
            return string.Empty;

        return str.Length > maxLength ? str.Substring(0, maxLength) + "..." : str;
    }
}