using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;
using DatabaseMcpServer.Services;

namespace DatabaseMcpServer.Configuration;

internal sealed class ConfigurationCommands
{
    private readonly ConfigurationFileStore _configFileService;
    private readonly ISqlSugarClientFactory? _clientFactory;
    private readonly DatabaseConnectionProbe? _connectionProbe;
    private readonly DatabaseExecutionCoordinator? _execution;
    private readonly DatabaseConfigurationApplicationService _editor;

    public ConfigurationCommands()
        : this(new ConfigurationFileStore())
    {
    }

    internal ConfigurationCommands(ConfigurationFileStore configFileService, ISqlSugarClientFactory? clientFactory = null,
        DatabaseConnectionProbe? connectionProbe = null, DatabaseExecutionCoordinator? execution = null)
    {
        _configFileService = configFileService;
        _clientFactory = clientFactory;
        _connectionProbe = connectionProbe;
        _execution = execution;
        _editor = new(configFileService);
    }

    public ConfigurationCommandResult Initialize(string? explicitConfigPath, bool force)
    {
        return Execute(() =>
        {
            var configPath = _configFileService.ResolveWritablePath(explicitConfigPath);
            var created = _configFileService.Initialize(configPath, force);
            if (!created && !force)
            {
                return new ConfigurationCommandResult
                {
                    Success = false,
                    Message = $"配置文件已存在: {configPath}。如需覆盖请追加 '--force'。",
                    Details = new()
                    {
                        ["configPath"] = configPath,
                        ["created"] = false
                    }
                };
            }

            return new ConfigurationCommandResult
            {
                Success = true,
                Message = created ? "已创建配置文件。" : "已覆盖现有配置文件。",
                Details = new()
                {
                    ["configPath"] = configPath,
                    ["created"] = created
                }
            };
        });
    }

    public ConfigurationCommandResult List(string? explicitConfigPath)
    {
        return Execute(() =>
        {
            var configPath = _configFileService.ResolveWritablePath(explicitConfigPath);
            var config = _configFileService.Load(configPath);
            var currentDefaultDatabase = ConfigurationFileStore.GetCurrentDefaultDatabaseName(config);

            return new ConfigurationCommandResult
            {
                Success = true,
                Details = new()
                {
                    ["configPath"] = configPath,
                    ["totalDatabases"] = config.Databases.Count,
                    ["currentDefaultDatabase"] = currentDefaultDatabase,
                    ["databases"] = config.Databases.Select(db => new
                    {
                        name = db.Name,
                        dbType = db.DbType,
                        description = db.Description,
                        isDefault = db.IsDefault,
                        enableDangerousOperations = db.EnableDangerousOperations
                    }).ToArray()
                }
            };
        });
    }

    public ConfigurationCommandResult Show(string? explicitConfigPath, string name)
    {
        return Execute(() =>
        {
            var configPath = _configFileService.ResolveWritablePath(explicitConfigPath);
            var config = _configFileService.Load(configPath);
            var connection = FindConnection(config, name);

            return new ConfigurationCommandResult
            {
                Success = true,
                Details = new()
                {
                    ["configPath"] = configPath,
                    ["database"] = ConfigurationFileStore.ToMaskedConnection(connection)
                }
            };
        });
    }

    public ConfigurationCommandResult ListPresets()
    {
        return Execute(() => new ConfigurationCommandResult
        {
            Success = true,
            Details = new()
            {
                ["totalPresets"] = DatabasePresetCatalog.Presets.Count,
                ["presets"] = DatabasePresetCatalog.Presets
                .OrderBy(item => item.DbType, StringComparer.OrdinalIgnoreCase)
                .Select(item => new
                {
                    dbType = item.DbType,
                    exampleName = item.ExampleName,
                    availability = item.Availability,
                    unavailableReason = item.UnavailableReason,
                    description = item.Description
                })
                .ToArray()
            }
        });
    }

    public ConfigurationCommandResult ShowPreset(string dbType)
    {
        return Execute(() =>
        {
            ValidateRequiredText(dbType, "db-type");
            if (!DatabasePresetCatalog.TryGet(dbType, out var preset))
            {
                return new ConfigurationCommandResult
                {
                    Success = false,
                    Message = $"未找到数据库类型 '{dbType}' 的内置模板。",
                    Details = new()
                    {
                        ["dbType"] = dbType
                    }
                };
            }

            return new ConfigurationCommandResult
            {
                Success = true,
                Details = new()
                {
                    ["preset"] = new
                    {
                        dbType = preset.DbType,
                        exampleName = preset.ExampleName,
                        availability = preset.Availability,
                        unavailableReason = preset.UnavailableReason,
                        exampleConnectionString = preset.ExampleConnectionString,
                        description = preset.Description
                    }
                }
            };
        });
    }

    public ConfigurationCommandResult CreateFromPreset(
        string? explicitConfigPath,
        string dbType,
        string? name,
        string? connectionString,
        string? description,
        bool setDefault,
        bool enableDangerousOperations,
        bool printOnly)
    {
        return Execute(() =>
        {
            ValidateRequiredText(dbType, "from-preset");
            if (!DatabasePresetCatalog.TryGet(dbType, out var preset))
            {
                return new ConfigurationCommandResult
                {
                    Success = false,
                    Message = $"未找到数据库类型 '{dbType}' 的内置模板。",
                    Details = new()
                    {
                        ["dbType"] = dbType
                    }
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
                EnableDangerousOperations = enableDangerousOperations
            };

            if (printOnly)
            {
                return new ConfigurationCommandResult
                {
                    Success = true,
                    Message = $"已生成 '{preset.DbType}' 模板连接预览，未写入配置文件。",
                    Details = new()
                    {
                        ["printOnly"] = true,
                        ["database"] = ConfigurationFileStore.ToMaskedConnection(database)
                    }
                };
            }

            var configPath = _configFileService.ResolveWritablePath(explicitConfigPath);
            using var write = new AtomicJsonFileWrite(configPath);
            var config = _configFileService.Load(configPath);

            if (config.Databases.Any(db => string.Equals(db.Name, databaseName, StringComparison.Ordinal)))
            {
                return new ConfigurationCommandResult
                {
                    Success = false,
                    Message = $"数据库连接 '{databaseName}' 已存在。",
                    Details = new()
                    {
                        ["configPath"] = configPath,
                        ["databaseName"] = databaseName,
                        ["dbType"] = preset.DbType
                    }
                };
            }

            if (setDefault)
            {
                ClearDefaultFlags(config);
            }

            config.Databases.Add(database);

            DatabaseConfigurationValidator.ThrowIfInvalid(config);
            write.Save(config);

            return new ConfigurationCommandResult
            {
                Success = true,
                Message = $"已基于 '{preset.DbType}' 模板创建数据库连接 '{databaseName}'。",
                Details = new()
                {
                    ["configPath"] = configPath,
                    ["databaseName"] = databaseName,
                    ["dbType"] = preset.DbType,
                    ["isDefault"] = setDefault,
                    ["enableDangerousOperations"] = enableDangerousOperations
                }
            };
        });
    }

    public ConfigurationCommandResult Add(
        string? explicitConfigPath,
        string name,
        string dbType,
        string connectionString,
        string? description,
        bool setDefault,
        bool enableDangerousOperations)
    {
        return Execute(() =>
        {
            ValidateRequiredText(name, "name");
            ValidateRequiredText(dbType, "db-type");
            ValidateRequiredText(connectionString, "connection-string");

            var configPath = _configFileService.ResolveWritablePath(explicitConfigPath);
            using var write = new AtomicJsonFileWrite(configPath);
            var config = _configFileService.Load(configPath);
            if (config.Databases.Any(db => string.Equals(db.Name, name, StringComparison.Ordinal)))
            {
                return new ConfigurationCommandResult
                {
                    Success = false,
                    Message = $"数据库连接 '{name}' 已存在。",
                    Details = new()
                    {
                        ["configPath"] = configPath,
                        ["databaseName"] = name
                    }
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
                EnableDangerousOperations = enableDangerousOperations
            });

            DatabaseConfigurationValidator.ThrowIfInvalid(config);
            write.Save(config);

            return new ConfigurationCommandResult
            {
                Success = true,
                Message = $"已新增数据库连接 '{name}'。",
                Details = new()
                {
                    ["configPath"] = configPath,
                    ["databaseName"] = name,
                    ["isDefault"] = setDefault,
                    ["enableDangerousOperations"] = enableDangerousOperations
                }
            };
        });
    }

    public ConfigurationCommandResult Rename(string? explicitConfigPath, string name, string newName, string? currentDatabaseStateFilePath = null)
    {
        return Execute(() =>
        {
            ValidateRequiredText(name, "name");
            ValidateRequiredText(newName, "new-name");

            var configPath = _configFileService.ResolveWritablePath(explicitConfigPath);
            var result = _editor.Edit(configPath, name,
                new DatabaseConnectionUpdate { NewName = newName }, _ =>
            {
                try
                {
                    var state = new CurrentDatabaseStateStore(
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<CurrentDatabaseStateStore>.Instance,
                        enabled: true, currentDatabaseStateFilePath);
                    var persisted = state.RenameCurrentDatabase(configPath, name, newName);
                    return new(true, true, false, persisted, $"已将数据库连接 '{name}' 重命名为 '{newName}'。");
                }
                catch (Exception ex)
                {
                    return new(false, true, false, false, $"重命名已保存，但当前选择未更新：{ex.Message}");
                }
            });

            return new ConfigurationCommandResult
            {
                Success = result.Success,
                Message = result.Message,
                Details = new()
                {
                    ["configPath"] = configPath,
                    ["previousName"] = name,
                    ["currentName"] = newName,
                    ["saved"] = result.Saved,
                    ["selectionPersisted"] = result.SelectionPersisted
                }
            };
        });
    }

    public ConfigurationCommandResult Update(
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
        bool enableDangerousOperations,
        bool hasEnableDangerousOperations,
        Func<string, string?>? buildConnectionString = null)
    {
        return Execute(() => FromMutation(_editor.Edit(
            _configFileService.ResolveWritablePath(explicitConfigPath), name, new DatabaseConnectionUpdate
            {
                DbType = dbType,
                ConnectionString = connectionString,
                Description = description,
                ClearDescription = clearDescription,
                SetDefault = setDefault,
                EnableDangerousOperations = enableDangerousOperations,
                ApplyDbType = hasDbType,
                ApplyConnectionString = hasConnectionString,
                ApplyDescription = hasDescription,
                ApplyClearDescription = hasClearDescription,
                ApplySetDefault = hasSetDefault,
                ApplyEnableDangerousOperations = hasEnableDangerousOperations,
                BuildConnectionString = buildConnectionString
            })));
    }

    public ConfigurationCommandResult Clone(string? explicitConfigPath, string name, string newName, bool setDefault)
    {
        return Execute(() =>
        {
            ValidateRequiredText(name, "name");
            ValidateRequiredText(newName, "new-name");

            var configPath = _configFileService.ResolveWritablePath(explicitConfigPath);
            using var write = new AtomicJsonFileWrite(configPath);
            var config = _configFileService.Load(configPath);
            var source = FindConnection(config, name);

            if (config.Databases.Any(db => string.Equals(db.Name, newName, StringComparison.Ordinal)))
            {
                return new ConfigurationCommandResult
                {
                    Success = false,
                    Message = $"数据库连接 '{newName}' 已存在。",
                    Details = new()
                    {
                        ["configPath"] = configPath,
                        ["sourceDatabaseName"] = name,
                        ["clonedDatabaseName"] = newName
                    }
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
                EnableDangerousOperations = source.EnableDangerousOperations,
                OptimizationSettings = source.OptimizationSettings == null
                    ? null
                    : new Dictionary<string, string>(source.OptimizationSettings, StringComparer.Ordinal)
            };

            config.Databases.Add(cloned);
            DatabaseConfigurationValidator.ThrowIfInvalid(config);
            write.Save(config);

            return new ConfigurationCommandResult
            {
                Success = true,
                Message = $"已从 '{name}' 克隆数据库连接 '{newName}'。",
                Details = new()
                {
                    ["configPath"] = configPath,
                    ["sourceDatabaseName"] = name,
                    ["clonedDatabaseName"] = newName,
                    ["isDefault"] = setDefault
                }
            };
        });
    }

    public ConfigurationCommandResult Remove(string? explicitConfigPath, string name)
    {
        return Execute(() =>
        {
            var configPath = _configFileService.ResolveWritablePath(explicitConfigPath);
            using var write = new AtomicJsonFileWrite(configPath);
            var config = _configFileService.Load(configPath);
            var removed = config.Databases.RemoveAll(db => string.Equals(db.Name, name, StringComparison.Ordinal));
            if (removed == 0)
            {
                return new ConfigurationCommandResult
                {
                    Success = false,
                    Message = $"数据库连接 '{name}' 不存在。",
                    Details = new()
                    {
                        ["configPath"] = configPath,
                        ["removedDatabaseName"] = name
                    }
                };
            }

            DatabaseConfigurationValidator.ThrowIfInvalid(config);
            write.Save(config);

            return new ConfigurationCommandResult
            {
                Success = true,
                Message = $"已删除数据库连接 '{name}'。",
                Details = new()
                {
                    ["configPath"] = configPath,
                    ["removedDatabaseName"] = name
                }
            };
        });
    }

    public ConfigurationCommandResult SetDefault(string? explicitConfigPath, string name)
    {
        return Execute(() =>
        {
            var configPath = _configFileService.ResolveWritablePath(explicitConfigPath);
            using var write = new AtomicJsonFileWrite(configPath);
            var config = _configFileService.Load(configPath);
            var target = FindConnection(config, name);

            ClearDefaultFlags(config);
            target.IsDefault = true;
            DatabaseConfigurationValidator.ThrowIfInvalid(config);
            write.Save(config);

            return new ConfigurationCommandResult
            {
                Success = true,
                Message = $"已将 '{name}' 设为默认数据库连接。",
                Details = new()
                {
                    ["configPath"] = configPath,
                    ["currentDefaultDatabase"] = name
                }
            };
        });
    }

    public ConfigurationCommandResult Use(string? explicitConfigPath, string name)
    {
        return Execute(() =>
        {
            var configPath = _configFileService.ResolveWritablePath(explicitConfigPath);
            using var write = new AtomicJsonFileWrite(configPath);
            var config = _configFileService.Load(configPath);
            var target = FindConnection(config, name);

            ClearDefaultFlags(config);
            target.IsDefault = true;
            DatabaseConfigurationValidator.ThrowIfInvalid(config);
            write.Save(config);

            return new ConfigurationCommandResult
            {
                Success = true,
                Message = $"当前默认数据库连接已切换为 '{name}'。",
                Details = new()
                {
                    ["configPath"] = configPath,
                    ["currentDefaultDatabase"] = name
                }
            };
        });
    }

    public ConfigurationCommandResult Validate(string? explicitConfigPath)
    {
        return Execute(() =>
        {
            var configPath = _configFileService.ResolveWritablePath(explicitConfigPath);
            var config = _configFileService.Load(configPath);
            var report = DatabaseConfigurationValidator.Validate(config);

            return new ConfigurationCommandResult
            {
                Success = report.Errors.Count == 0,
                Details = new()
                {
                    ["configPath"] = configPath,
                    ["totalDatabases"] = config.Databases?.Count ?? 0,
                    ["defaultDatabaseCount"] = report.DefaultDatabaseCount,
                    ["errors"] = report.Errors,
                    ["warnings"] = report.Warnings
                }
            };
        });
    }

    private static SqlSugarClientFactory CreateDiagnosticFactory() => new(
        Microsoft.Extensions.Logging.Abstractions.NullLogger<SqlSugarClientFactory>.Instance,
        new DatabaseMcpServer.Strategies.DBSetting.DatabaseOptimizationStrategyFactory(), DatabaseExecutionOptions.FromEnvironment());

    public async Task<ConfigurationCommandResult> DoctorAsync(string? explicitConfigPath, string? name, bool testConnections, bool includeFixSuggestions, bool summaryOnly,
        CancellationToken cancellationToken = default)
    {
        var configPath = string.Empty;
        try
        {
            configPath = _configFileService.ResolveWritablePath(explicitConfigPath);
            var config = _configFileService.Load(configPath);
            var report = DatabaseConfigurationValidator.Validate(config, requireConnections: testConnections);
            if (report.Errors.Count == 0 && !string.IsNullOrWhiteSpace(name))
            {
                var selected = FindConnection(config, name);
                config = new DatabasesConfig
                {
                    Databases = [selected]
                };
            }
            var fixSuggestions = includeFixSuggestions
                ? BuildFixSuggestions(report.Errors, null)
                : Array.Empty<string>();

            if (!testConnections || report.Errors.Count > 0)
            {
                return new ConfigurationCommandResult
                {
                    Success = report.Errors.Count == 0,
                    Details = new()
                    {
                        ["configPath"] = configPath,
                        ["databaseName"] = name,
                        ["totalDatabases"] = config.Databases?.Count ?? 0,
                        ["defaultDatabaseCount"] = report.DefaultDatabaseCount,
                        ["testedConnections"] = 0,
                        ["skippedConnectionTests"] = !testConnections || report.Errors.Count > 0,
                        ["skippedReason"] = !testConnections
                        ? "已显式关闭连通性测试。"
                        : "配置校验未通过，已跳过连通性测试。",
                        ["configErrors"] = summaryOnly ? null : report.Errors,
                        ["configWarnings"] = report.Warnings,
                        ["fixSuggestions"] = summaryOnly ? null : fixSuggestions,
                        ["connectionResults"] = summaryOnly ? null : Array.Empty<object>()
                    }
                };
            }

            var connectionResults = new List<object>();
            using var ownedFactory = _clientFactory == null ? CreateDiagnosticFactory() : null;
            using var ownedExecution = _execution == null ? new DatabaseExecutionCoordinator(DatabaseExecutionOptions.FromEnvironment()) : null;
            var execution = _execution ?? ownedExecution!;
            using var ownedMongo = _connectionProbe == null ? new MongoClientRegistry() : null;
            var probe = _connectionProbe ?? new DatabaseConnectionProbe(ownedMongo!, DatabaseExecutionOptions.FromEnvironment(), _clientFactory ?? ownedFactory!);
            foreach (var connection in config.Databases)
            {
                var result = await DatabaseConnectionDiagnostics.CheckAsync(execution, connection, probe, cancellationToken);
                connectionResults.Add(new DoctorConnectionResult(connection.Name, connection.DbType, result.Connected, result.Message));
            }

            var failedConnections = connectionResults.Count(item =>
                item is DoctorConnectionResult result && !result.Connected);
            fixSuggestions = includeFixSuggestions
                ? BuildFixSuggestions(report.Errors, connectionResults.Cast<DoctorConnectionResult>().ToArray())
                : Array.Empty<string>();

            return new ConfigurationCommandResult
            {
                Success = report.Errors.Count == 0 && failedConnections == 0,
                Details = new()
                {
                    ["configPath"] = configPath,
                    ["databaseName"] = name,
                    ["totalDatabases"] = config.Databases.Count,
                    ["defaultDatabaseCount"] = report.DefaultDatabaseCount,
                    ["testedConnections"] = connectionResults.Count,
                    ["failedConnections"] = failedConnections,
                    ["skippedConnectionTests"] = false,
                    ["skippedReason"] = string.Empty,
                    ["configErrors"] = summaryOnly ? null : report.Errors,
                    ["configWarnings"] = report.Warnings,
                    ["fixSuggestions"] = summaryOnly ? null : fixSuggestions,
                    ["connectionResults"] = summaryOnly ? null : connectionResults
                }
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            return new ConfigurationCommandResult
            {
                Success = false,
                Message = ex.Message,
                Details = new()
                {
                    ["configPath"] = configPath
                }
            };
        }
    }

    public async Task<ConfigurationCommandResult> TestAsync(string? explicitConfigPath, string name, CancellationToken cancellationToken = default)
    {
        var configPath = string.Empty;
        try
        {
            configPath = _configFileService.ResolveWritablePath(explicitConfigPath);
            var connection = FindConnection(_configFileService.Load(configPath), name);
            using var ownedFactory = _clientFactory == null ? CreateDiagnosticFactory() : null;
            using var ownedExecution = _execution == null ? new DatabaseExecutionCoordinator(DatabaseExecutionOptions.FromEnvironment()) : null;
            var execution = _execution ?? ownedExecution!;
            using var ownedMongo = _connectionProbe == null ? new MongoClientRegistry() : null;
            var probe = _connectionProbe ?? new DatabaseConnectionProbe(ownedMongo!, DatabaseExecutionOptions.FromEnvironment(), _clientFactory ?? ownedFactory!);
            var result = await DatabaseConnectionDiagnostics.CheckAsync(execution, connection, probe, cancellationToken);
            return new ConfigurationCommandResult
            {
                Success = result.Connected,
                Message = result.Message,
                Details = new()
                {
                    ["connected"] = result.Connected,
                    ["databaseName"] = name
                }
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            return new ConfigurationCommandResult
            {
                Success = false,
                Message = ex.Message,
                Details = new()
                {
                    ["configPath"] = configPath,
                    ["databaseName"] = name
                }
            };
        }
    }

    public ConfigurationCommandResult Export(string? explicitConfigPath, string outputPath, bool force)
    {
        return Execute(() =>
        {
            ValidateRequiredText(outputPath, "output");

            var sourceConfigPath = _configFileService.ResolveWritablePath(explicitConfigPath);
            var targetOutputPath = Path.GetFullPath(outputPath);
            using var write = new AtomicJsonFileWrite(targetOutputPath);
            var config = _configFileService.Load(sourceConfigPath);

            if (write.Exists && !force)
            {
                return new ConfigurationCommandResult
                {
                    Success = false,
                    Message = $"导出目标已存在: {targetOutputPath}。如需覆盖请追加 '--force'。",
                    Details = new()
                    {
                        ["sourceConfigPath"] = sourceConfigPath,
                        ["outputPath"] = targetOutputPath
                    }
                };
            }

            write.Save(config);

            return new ConfigurationCommandResult
            {
                Success = true,
                Message = "配置导出成功。",
                Details = new()
                {
                    ["sourceConfigPath"] = sourceConfigPath,
                    ["outputPath"] = targetOutputPath,
                    ["totalDatabases"] = config.Databases.Count
                }
            };
        });
    }

    public ConfigurationCommandResult Import(string? explicitConfigPath, string inputPath, bool force)
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

            using var write = new AtomicJsonFileWrite(targetConfigPath);
            if (write.Exists && !force)
            {
                return new ConfigurationCommandResult
                {
                    Success = false,
                    Message = $"目标配置文件已存在: {targetConfigPath}。如需覆盖请追加 '--force'。",
                    Details = new()
                    {
                        ["inputPath"] = sourceInputPath,
                        ["configPath"] = targetConfigPath
                    }
                };
            }

            var importedConfig = _configFileService.Load(sourceInputPath);
            DatabaseConfigurationValidator.ThrowIfInvalid(importedConfig);
            write.Save(importedConfig);

            return new ConfigurationCommandResult
            {
                Success = true,
                Message = "配置导入成功。",
                Details = new()
                {
                    ["inputPath"] = sourceInputPath,
                    ["configPath"] = targetConfigPath,
                    ["totalDatabases"] = importedConfig.Databases.Count
                }
            };
        });
    }

    private static ConfigurationCommandResult FromMutation(ConfigurationMutationResult result)
    {
        var response = new ConfigurationCommandResult
        {
            Success = result.Success,
            Message = result.Message,
            Details = new()
            {
                ["saved"] = result.Saved,
                ["runtimeApplied"] = result.RuntimeApplied,
                ["selectionPersisted"] = result.SelectionPersisted,
                ["currentDatabase"] = result.CurrentDatabase
            }
        };
        if (result.ConfigPath != null) response.Details["configPath"] = result.ConfigPath;
        if (result.DatabaseName != null) response.Details["databaseName"] = result.DatabaseName;
        if (result.Database != null) response.Details["database"] = result.Database;
        return response;
    }

    private ConfigurationCommandResult Execute(Func<ConfigurationCommandResult> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex)
        {
            return new ConfigurationCommandResult
            {
                Success = false,
                Message = ex.Message
            };
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


    private sealed record DoctorConnectionResult(
        string DatabaseName,
        string DatabaseType,
        bool Connected,
        string Message);
}
