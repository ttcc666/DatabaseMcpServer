using DatabaseMcpServer.Hosting;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;
using DatabaseMcpServer.Tools.Management;
using Microsoft.Extensions.DependencyInjection;

namespace DatabaseMcpServer.Cli;

internal sealed class CliConfigCommandHandler
{
    private readonly IJsonResultSerializer _serializer;
    private readonly CliConfigFileService _configFileService;

    public CliConfigCommandHandler()
        : this(new JsonResultSerializer(), new CliConfigFileService())
    {
    }

    internal CliConfigCommandHandler(IJsonResultSerializer serializer, CliConfigFileService configFileService)
    {
        _serializer = serializer;
        _configFileService = configFileService;
    }

    public string Initialize(string? explicitConfigPath, bool force)
    {
        return Execute(() =>
        {
            var configPath = _configFileService.ResolveWritablePath(explicitConfigPath);
            var created = _configFileService.Initialize(configPath, force);
            if (!created && !force)
            {
                return new
                {
                    success = false,
                    message = $"配置文件已存在: {configPath}。如需覆盖请追加 '--force'。",
                    configPath,
                    created = false
                };
            }

            return new
            {
                success = true,
                message = created ? "已创建配置文件。" : "已覆盖现有配置文件。",
                configPath,
                created
            };
        });
    }

    public string List(string? explicitConfigPath)
    {
        return Execute(() =>
        {
            var configPath = _configFileService.ResolveWritablePath(explicitConfigPath);
            var config = _configFileService.Load(configPath);
            var currentDefaultDatabase = CliConfigFileService.GetCurrentDefaultDatabaseName(config);

            return new
            {
                success = true,
                configPath,
                totalDatabases = config.Databases.Count,
                currentDefaultDatabase,
                databases = config.Databases.Select(db => new
                {
                    name = db.Name,
                    dbType = db.DbType,
                    description = db.Description,
                    isDefault = db.IsDefault,
                    allowDangerousOperations = db.AllowDangerousOperations
                }).ToArray()
            };
        });
    }

    public string Show(string? explicitConfigPath, string name)
    {
        return Execute(() =>
        {
            var configPath = _configFileService.ResolveWritablePath(explicitConfigPath);
            var config = _configFileService.Load(configPath);
            var connection = FindConnection(config, name);

            return new
            {
                success = true,
                configPath,
                database = CliConfigFileService.ToMaskedConnection(connection)
            };
        });
    }

    public string ListPresets()
    {
        return Execute(() => new
        {
            success = true,
            totalPresets = CliConfigPresetCatalog.Presets.Count,
            presets = CliConfigPresetCatalog.Presets
                .OrderBy(item => item.DbType, StringComparer.OrdinalIgnoreCase)
                .Select(item => new
                {
                    dbType = item.DbType,
                    exampleName = item.ExampleName,
                    description = item.Description
                })
                .ToArray()
        });
    }

    public string ShowPreset(string dbType)
    {
        return Execute(() =>
        {
            ValidateRequiredText(dbType, "db-type");
            if (!CliConfigPresetCatalog.TryGet(dbType, out var preset))
            {
                return new
                {
                    success = false,
                    message = $"未找到数据库类型 '{dbType}' 的内置模板。",
                    dbType
                };
            }

            return new
            {
                success = true,
                preset = new
                {
                    dbType = preset.DbType,
                    exampleName = preset.ExampleName,
                    exampleConnectionString = preset.ExampleConnectionString,
                    description = preset.Description
                }
            };
        });
    }

    public string CreateFromPreset(
        string? explicitConfigPath,
        string dbType,
        string? name,
        string? connectionString,
        string? description,
        bool setDefault,
        bool allowDangerousOperations,
        bool printOnly)
    {
        return Execute(() =>
        {
            ValidateRequiredText(dbType, "from-preset");
            if (!CliConfigPresetCatalog.TryGet(dbType, out var preset))
            {
                return new
                {
                    success = false,
                    message = $"未找到数据库类型 '{dbType}' 的内置模板。",
                    dbType
                };
            }

            var databaseName = string.IsNullOrWhiteSpace(name) ? preset.ExampleName : name;
            var database = new DatabaseConnection
            {
                Name = databaseName,
                DbType = preset.DbType,
                ConnectionString = string.IsNullOrWhiteSpace(connectionString) ? preset.ExampleConnectionString : connectionString,
                Description = description ?? preset.Description,
                IsDefault = setDefault,
                AllowDangerousOperations = allowDangerousOperations
            };

            if (printOnly)
            {
                return new
                {
                    success = true,
                    printOnly = true,
                    message = $"已生成 '{preset.DbType}' 模板连接预览，未写入配置文件。",
                    database = CliConfigFileService.ToMaskedConnection(database)
                };
            }

            var configPath = _configFileService.ResolveWritablePath(explicitConfigPath);
            var config = _configFileService.Load(configPath);

            if (config.Databases.Any(db => string.Equals(db.Name, databaseName, StringComparison.Ordinal)))
            {
                return new
                {
                    success = false,
                    message = $"数据库连接 '{databaseName}' 已存在。",
                    configPath,
                    databaseName,
                    dbType = preset.DbType
                };
            }

            if (setDefault)
            {
                ClearDefaultFlags(config);
            }

            config.Databases.Add(database);

            _configFileService.Save(configPath, config);

            return new
            {
                success = true,
                message = $"已基于 '{preset.DbType}' 模板创建数据库连接 '{databaseName}'。",
                configPath,
                databaseName,
                dbType = preset.DbType,
                isDefault = setDefault,
                allowDangerousOperations
            };
        });
    }

    public string Add(
        string? explicitConfigPath,
        string name,
        string dbType,
        string connectionString,
        string? description,
        bool setDefault,
        bool allowDangerousOperations)
    {
        return Execute(() =>
        {
            ValidateRequiredText(name, "name");
            ValidateRequiredText(dbType, "db-type");
            ValidateRequiredText(connectionString, "connection-string");

            var configPath = _configFileService.ResolveWritablePath(explicitConfigPath);
            var config = _configFileService.Load(configPath);
            if (config.Databases.Any(db => string.Equals(db.Name, name, StringComparison.Ordinal)))
            {
                return new
                {
                    success = false,
                    message = $"数据库连接 '{name}' 已存在。",
                    configPath,
                    databaseName = name
                };
            }

            if (setDefault)
            {
                ClearDefaultFlags(config);
            }

            config.Databases.Add(new DatabaseConnection
            {
                Name = name,
                DbType = dbType,
                ConnectionString = connectionString,
                Description = description,
                IsDefault = setDefault,
                AllowDangerousOperations = allowDangerousOperations
            });

            _configFileService.Save(configPath, config);

            return new
            {
                success = true,
                message = $"已新增数据库连接 '{name}'。",
                configPath,
                databaseName = name,
                isDefault = setDefault,
                allowDangerousOperations
            };
        });
    }

    public string Rename(string? explicitConfigPath, string name, string newName)
    {
        return Execute(() =>
        {
            ValidateRequiredText(name, "name");
            ValidateRequiredText(newName, "new-name");

            var configPath = _configFileService.ResolveWritablePath(explicitConfigPath);
            var config = _configFileService.Load(configPath);
            var target = FindConnection(config, name);

            if (string.Equals(name, newName, StringComparison.Ordinal))
            {
                return new
                {
                    success = true,
                    message = $"数据库连接名称未变化，仍为 '{name}'。",
                    configPath,
                    previousName = name,
                    currentName = newName
                };
            }

            if (config.Databases.Any(db => string.Equals(db.Name, newName, StringComparison.Ordinal)))
            {
                return new
                {
                    success = false,
                    message = $"数据库连接 '{newName}' 已存在。",
                    configPath,
                    previousName = name,
                    attemptedName = newName
                };
            }

            target.Name = newName;
            _configFileService.Save(configPath, config);

            return new
            {
                success = true,
                message = $"已将数据库连接 '{name}' 重命名为 '{newName}'。",
                configPath,
                previousName = name,
                currentName = newName
            };
        });
    }

    public string Update(
        string? explicitConfigPath,
        string name,
        string? dbType,
        string? connectionString,
        string? description,
        bool clearDescription,
        bool setDefault,
        bool hasDbType,
        bool hasConnectionString,
        bool hasDescription,
        bool hasClearDescription,
        bool hasSetDefault,
        bool allowDangerousOperations,
        bool hasAllowDangerousOperations)
    {
        return Execute(() =>
        {
            ValidateRequiredText(name, "name");

            var configPath = _configFileService.ResolveWritablePath(explicitConfigPath);
            var config = _configFileService.Load(configPath);
            var target = FindConnection(config, name);

            if (!hasDbType && !hasConnectionString && !hasDescription && !hasClearDescription && !hasSetDefault && !hasAllowDangerousOperations)
            {
                return new
                {
                    success = false,
                    message = "至少需要提供一个可更新选项：--db-type / --connection-string / --description / --clear-description / --set-default / --allow-dangerous-operations。",
                    configPath,
                    databaseName = name
                };
            }

            if (hasDescription && hasClearDescription && clearDescription)
            {
                return new
                {
                    success = false,
                    message = "不能同时使用 '--description' 和 '--clear-description true'。",
                    configPath,
                    databaseName = name
                };
            }

            if (hasDbType)
            {
                ValidateRequiredText(dbType!, "db-type");
                target.DbType = dbType!;
            }

            if (hasConnectionString)
            {
                ValidateRequiredText(connectionString!, "connection-string");
                target.ConnectionString = connectionString!;
            }

            if (hasDescription)
            {
                target.Description = description;
            }
            else if (hasClearDescription && clearDescription)
            {
                target.Description = null;
            }

            if (hasSetDefault && setDefault)
            {
                ClearDefaultFlags(config);
                target.IsDefault = true;
            }
            else if (hasSetDefault)
            {
                target.IsDefault = false;
            }

            if (hasAllowDangerousOperations)
            {
                target.AllowDangerousOperations = allowDangerousOperations;
            }

            _configFileService.Save(configPath, config);

            return new
            {
                success = true,
                message = $"已更新数据库连接 '{name}'。",
                configPath,
                database = CliConfigFileService.ToMaskedConnection(target)
            };
        });
    }

    public string Clone(string? explicitConfigPath, string name, string newName, bool setDefault)
    {
        return Execute(() =>
        {
            ValidateRequiredText(name, "name");
            ValidateRequiredText(newName, "new-name");

            var configPath = _configFileService.ResolveWritablePath(explicitConfigPath);
            var config = _configFileService.Load(configPath);
            var source = FindConnection(config, name);

            if (config.Databases.Any(db => string.Equals(db.Name, newName, StringComparison.Ordinal)))
            {
                return new
                {
                    success = false,
                    message = $"数据库连接 '{newName}' 已存在。",
                    configPath,
                    sourceDatabaseName = name,
                    clonedDatabaseName = newName
                };
            }

            if (setDefault)
            {
                ClearDefaultFlags(config);
            }

            var cloned = new DatabaseConnection
            {
                Name = newName,
                ConnectionString = source.ConnectionString,
                DbType = source.DbType,
                Description = source.Description,
                IsDefault = setDefault,
                AllowDangerousOperations = source.AllowDangerousOperations,
                OptimizationSettings = source.OptimizationSettings == null
                    ? null
                    : new Dictionary<string, string>(source.OptimizationSettings, StringComparer.Ordinal)
            };

            config.Databases.Add(cloned);
            _configFileService.Save(configPath, config);

            return new
            {
                success = true,
                message = $"已从 '{name}' 克隆数据库连接 '{newName}'。",
                configPath,
                sourceDatabaseName = name,
                clonedDatabaseName = newName,
                isDefault = setDefault
            };
        });
    }

    public string Remove(string? explicitConfigPath, string name)
    {
        return Execute(() =>
        {
            var configPath = _configFileService.ResolveWritablePath(explicitConfigPath);
            var config = _configFileService.Load(configPath);
            var removed = config.Databases.RemoveAll(db => string.Equals(db.Name, name, StringComparison.Ordinal));
            if (removed == 0)
            {
                return new
                {
                    success = false,
                    message = $"数据库连接 '{name}' 不存在。",
                    configPath,
                    removedDatabaseName = name
                };
            }

            _configFileService.Save(configPath, config);

            return new
            {
                success = true,
                message = $"已删除数据库连接 '{name}'。",
                configPath,
                removedDatabaseName = name
            };
        });
    }

    public string SetDefault(string? explicitConfigPath, string name)
    {
        return Execute(() =>
        {
            var configPath = _configFileService.ResolveWritablePath(explicitConfigPath);
            var config = _configFileService.Load(configPath);
            var target = FindConnection(config, name);

            ClearDefaultFlags(config);
            target.IsDefault = true;
            _configFileService.Save(configPath, config);

            return new
            {
                success = true,
                message = $"已将 '{name}' 设为默认数据库连接。",
                configPath,
                currentDefaultDatabase = name
            };
        });
    }

    public string Use(string? explicitConfigPath, string name)
    {
        return Execute(() =>
        {
            var configPath = _configFileService.ResolveWritablePath(explicitConfigPath);
            var config = _configFileService.Load(configPath);
            var target = FindConnection(config, name);

            ClearDefaultFlags(config);
            target.IsDefault = true;
            _configFileService.Save(configPath, config);

            return new
            {
                success = true,
                message = $"当前默认数据库连接已切换为 '{name}'。",
                configPath,
                currentDefaultDatabase = name
            };
        });
    }

    public string Validate(string? explicitConfigPath)
    {
        return Execute(() =>
        {
            var configPath = _configFileService.ResolveWritablePath(explicitConfigPath);
            var config = _configFileService.Load(configPath);
            var report = BuildValidationReport(config);

            return new
            {
                success = report.Errors.Count == 0,
                configPath,
                totalDatabases = config.Databases.Count,
                defaultDatabaseCount = report.DefaultDatabaseCount,
                errors = report.Errors
            };
        });
    }

    public async Task<string> DoctorAsync(string? explicitConfigPath, string? name, bool testConnections, bool includeFixSuggestions, bool summaryOnly)
    {
        var configPath = string.Empty;
        try
        {
            configPath = _configFileService.ResolveWritablePath(explicitConfigPath);
            var config = _configFileService.Load(configPath);
            if (!string.IsNullOrWhiteSpace(name))
            {
                var selected = FindConnection(config, name);
                config = new DatabasesConfig
                {
                    Databases = [selected]
                };
            }
            var report = BuildValidationReport(config);
            var fixSuggestions = includeFixSuggestions
                ? BuildFixSuggestions(report.Errors, null)
                : Array.Empty<string>();

            if (!testConnections || report.Errors.Count > 0)
            {
                return _serializer.Serialize(new
                {
                    success = report.Errors.Count == 0,
                    configPath,
                    databaseName = name,
                    totalDatabases = config.Databases.Count,
                    defaultDatabaseCount = report.DefaultDatabaseCount,
                    testedConnections = 0,
                    skippedConnectionTests = !testConnections || report.Errors.Count > 0,
                    skippedReason = !testConnections
                        ? "已显式关闭连通性测试。"
                        : "配置校验未通过，已跳过连通性测试。",
                    configErrors = summaryOnly ? null : report.Errors,
                    fixSuggestions = summaryOnly ? null : fixSuggestions,
                    connectionResults = summaryOnly ? null : Array.Empty<object>()
                });
            }

            var connectionResults = new List<object>();
            var originalConfigPath = Environment.GetEnvironmentVariable("DB_CONFIG_PATH");
            var originalConsoleOut = Console.Out;
            var originalConsoleError = Console.Error;

            try
            {
                Environment.SetEnvironmentVariable("DB_CONFIG_PATH", configPath);
                Console.SetOut(TextWriter.Null);
                Console.SetError(TextWriter.Null);

                var builder = DatabaseHostBuilderFactory.CreateBaseBuilder([], silentLogs: true);
                using var host = builder.Build();
                var databaseConfig = host.Services.GetRequiredService<IDatabaseConfigService>();

                foreach (var connection in config.Databases)
                {
                    try
                    {
                        var db = databaseConfig.CreateClient(connection.Name);
                        db.Ado.CheckConnection();
                        connectionResults.Add(new DoctorConnectionResult(
                            connection.Name,
                            connection.DbType,
                            true,
                            "连接成功"));
                    }
                    catch (Exception ex)
                    {
                        connectionResults.Add(new DoctorConnectionResult(
                            connection.Name,
                            connection.DbType,
                            false,
                            ex.Message));
                    }
                }
            }
            finally
            {
                Console.SetOut(originalConsoleOut);
                Console.SetError(originalConsoleError);
                Environment.SetEnvironmentVariable("DB_CONFIG_PATH", originalConfigPath);
            }

            var failedConnections = connectionResults.Count(item =>
                item is DoctorConnectionResult result && !result.Connected);
            fixSuggestions = includeFixSuggestions
                ? BuildFixSuggestions(report.Errors, connectionResults.Cast<DoctorConnectionResult>().ToArray())
                : Array.Empty<string>();

            return _serializer.Serialize(new
            {
                success = report.Errors.Count == 0 && failedConnections == 0,
                configPath,
                databaseName = name,
                totalDatabases = config.Databases.Count,
                defaultDatabaseCount = report.DefaultDatabaseCount,
                testedConnections = connectionResults.Count,
                failedConnections,
                skippedConnectionTests = false,
                skippedReason = string.Empty,
                configErrors = summaryOnly ? null : report.Errors,
                fixSuggestions = summaryOnly ? null : fixSuggestions,
                connectionResults = summaryOnly ? null : connectionResults
            });
        }
        catch (Exception ex)
        {
            return _serializer.Serialize(new
            {
                success = false,
                message = ex.Message,
                configPath
            });
        }
    }

    public async Task<string> TestAsync(string? explicitConfigPath, string name)
    {
        var configPath = string.Empty;
        try
        {
            configPath = _configFileService.ResolveWritablePath(explicitConfigPath);
            _ = FindConnection(_configFileService.Load(configPath), name);

            var originalConfigPath = Environment.GetEnvironmentVariable("DB_CONFIG_PATH");
            var originalConsoleOut = Console.Out;
            var originalConsoleError = Console.Error;

            try
            {
                Environment.SetEnvironmentVariable("DB_CONFIG_PATH", configPath);
                Console.SetOut(TextWriter.Null);
                Console.SetError(TextWriter.Null);

                var builder = DatabaseHostBuilderFactory.CreateBaseBuilder([], silentLogs: true);
                using var host = builder.Build();
                var tool = host.Services.GetRequiredService<ConnectionTools>();
                return await Task.FromResult(tool.TestConnectionByName(name));
            }
            finally
            {
                Console.SetOut(originalConsoleOut);
                Console.SetError(originalConsoleError);
                Environment.SetEnvironmentVariable("DB_CONFIG_PATH", originalConfigPath);
            }
        }
        catch (Exception ex)
        {
            return _serializer.Serialize(new
            {
                success = false,
                message = ex.Message,
                configPath,
                databaseName = name
            });
        }
    }

    public string Export(string? explicitConfigPath, string outputPath, bool force)
    {
        return Execute(() =>
        {
            ValidateRequiredText(outputPath, "output");

            var sourceConfigPath = _configFileService.ResolveWritablePath(explicitConfigPath);
            var targetOutputPath = Path.GetFullPath(outputPath);
            var config = _configFileService.Load(sourceConfigPath);

            if (File.Exists(targetOutputPath) && !force)
            {
                return new
                {
                    success = false,
                    message = $"导出目标已存在: {targetOutputPath}。如需覆盖请追加 '--force'。",
                    sourceConfigPath,
                    outputPath = targetOutputPath
                };
            }

            _configFileService.Save(targetOutputPath, config);

            return new
            {
                success = true,
                message = "配置导出成功。",
                sourceConfigPath,
                outputPath = targetOutputPath,
                totalDatabases = config.Databases.Count
            };
        });
    }

    public string Import(string? explicitConfigPath, string inputPath, bool force)
    {
        return Execute(() =>
        {
            ValidateRequiredText(inputPath, "input");

            var sourceInputPath = Path.GetFullPath(inputPath);
            if (!File.Exists(sourceInputPath))
            {
                throw new InvalidOperationException($"导入源文件不存在: {sourceInputPath}");
            }

            var targetConfigPath = _configFileService.ResolveWritablePath(explicitConfigPath);
            if (string.Equals(sourceInputPath, targetConfigPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("导入源文件与目标配置文件相同，无需导入。");
            }

            if (File.Exists(targetConfigPath) && !force)
            {
                return new
                {
                    success = false,
                    message = $"目标配置文件已存在: {targetConfigPath}。如需覆盖请追加 '--force'。",
                    inputPath = sourceInputPath,
                    configPath = targetConfigPath
                };
            }

            var importedConfig = _configFileService.Load(sourceInputPath);
            _configFileService.Save(targetConfigPath, importedConfig);

            return new
            {
                success = true,
                message = "配置导入成功。",
                inputPath = sourceInputPath,
                configPath = targetConfigPath,
                totalDatabases = importedConfig.Databases.Count
            };
        });
    }

    private string Execute(Func<object> action)
    {
        try
        {
            return _serializer.Serialize(action());
        }
        catch (Exception ex)
        {
            return _serializer.Serialize(new
            {
                success = false,
                message = ex.Message
            });
        }
    }

    private static DatabaseConnection FindConnection(DatabasesConfig config, string name)
    {
        var connection = config.Databases.FirstOrDefault(db => string.Equals(db.Name, name, StringComparison.Ordinal));
        if (connection == null)
        {
            throw new InvalidOperationException($"数据库连接 '{name}' 不存在。");
        }

        return connection;
    }

    private static void ClearDefaultFlags(DatabasesConfig config)
    {
        foreach (var item in config.Databases)
        {
            item.IsDefault = false;
        }
    }

    private static void ValidateRequiredText(string value, string optionName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"选项 '--{optionName}' 不能为空。");
        }
    }

    private static ValidationReport BuildValidationReport(DatabasesConfig config)
    {
        var errors = new List<string>();
        var duplicateNames = config.Databases
            .Where(db => !string.IsNullOrWhiteSpace(db.Name))
            .GroupBy(db => db.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        if (duplicateNames.Length > 0)
        {
            errors.Add($"存在重复的数据库连接名称: {string.Join(", ", duplicateNames)}");
        }

        var defaultCount = config.Databases.Count(db => db.IsDefault);
        if (defaultCount > 1)
        {
            errors.Add($"默认数据库连接数量超过 1 个，当前为 {defaultCount} 个。");
        }

        for (var i = 0; i < config.Databases.Count; i++)
        {
            var item = config.Databases[i];
            if (string.IsNullOrWhiteSpace(item.Name))
            {
                errors.Add($"第 {i + 1} 个数据库连接缺少 name。");
            }

            if (string.IsNullOrWhiteSpace(item.DbType))
            {
                errors.Add($"数据库连接 '{item.Name}' 缺少 dbType。");
            }

            if (string.IsNullOrWhiteSpace(item.ConnectionString))
            {
                errors.Add($"数据库连接 '{item.Name}' 缺少 connectionString。");
            }
        }

        return new ValidationReport(defaultCount, errors);
    }

    private static string[] BuildFixSuggestions(IReadOnlyCollection<string> configErrors, IReadOnlyCollection<DoctorConnectionResult>? connectionResults)
    {
        var suggestions = new List<string>();

        if (configErrors.Any(error => error.Contains("重复", StringComparison.Ordinal)))
        {
            suggestions.Add("先执行 'config rename' 或删除重复项，保证每个 name 唯一。");
        }

        if (configErrors.Any(error => error.Contains("缺少 name", StringComparison.Ordinal)))
        {
            suggestions.Add("补齐缺失的 name，或删除无效连接项。");
        }

        if (configErrors.Any(error => error.Contains("缺少 dbType", StringComparison.Ordinal)))
        {
            suggestions.Add("使用 'config update --db-type <type>' 补齐数据库类型。");
        }

        if (configErrors.Any(error => error.Contains("缺少 connectionString", StringComparison.Ordinal)))
        {
            suggestions.Add("使用 'config update --connection-string <value>' 补齐连接字符串。");
        }

        if (configErrors.Any(error => error.Contains("默认数据库连接数量超过 1", StringComparison.Ordinal)))
        {
            suggestions.Add("执行 'config use --name <name>' 或 'config set-default --name <name>' 只保留一个默认连接。");
        }

        if (connectionResults != null && connectionResults.Any(result => !result.Connected))
        {
            suggestions.Add("对失败连接先执行 'config show --name <name>' 检查连接串，再单独执行 'config test --name <name>' 复测。");
        }

        if (suggestions.Count == 0)
        {
            suggestions.Add("未发现明显配置问题；若仍有异常，请单独运行 'config test --name <name>' 诊断具体连接。");
        }

        return suggestions.Distinct(StringComparer.Ordinal).ToArray();
    }

    private sealed record ValidationReport(int DefaultDatabaseCount, List<string> Errors);

    private sealed record DoctorConnectionResult(
        string DatabaseName,
        string DatabaseType,
        bool Connected,
        string Message);
}
