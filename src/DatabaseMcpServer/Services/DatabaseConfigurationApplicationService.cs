using DatabaseMcpServer.Cli;
using DatabaseMcpServer.Helpers;
using DatabaseMcpServer.Models;

namespace DatabaseMcpServer.Services;

/// <summary>One candidate, validation and file write shared by CLI and runtime configuration edits.</summary>
internal sealed class DatabaseConfigurationApplicationService(CliConfigFileService files)
{
    public ConfigurationMutationResult Edit(string path, string name, DatabaseConnectionUpdate update,
        Func<DatabasesConfig, ConfigurationMutationResult>? apply = null)
    {
        var saved = false;
        DatabaseConnection? target = null;
        try
        {
            using var write = new AtomicJsonFileWrite(path);
            var config = files.Load(path);
            target = config.Databases.FirstOrDefault(db => db.Name == name)
                ?? throw new InvalidOperationException($"数据库连接 '{name}' 不存在。");
            if (update.NewName == null && !update.ApplyDbType && !update.ApplyConnectionString && !update.ApplyDescription
                && !update.ApplyClearDescription && !update.ApplySetDefault && !update.ApplyEnableDangerousOperations)
                throw new InvalidOperationException("至少需要提供一个可更新选项。");
            if (update.ApplyDescription && update.ApplyClearDescription && update.ClearDescription)
                throw new InvalidOperationException("不能同时使用 '--description' 和 '--clear-description true'。");
            if (update.NewName != null) target.Name = update.NewName;
            if (update.ApplyDbType) target.DbType = update.DbType ?? string.Empty;
            if (update.ApplyConnectionString)
                target.ConnectionString = update.BuildConnectionString?.Invoke(target.DbType) ?? update.ConnectionString ?? string.Empty;
            if (update.ApplyDescription) target.Description = update.Description;
            else if (update.ApplyClearDescription && update.ClearDescription) target.Description = null;
            if (update.ApplySetDefault)
            {
                if (update.SetDefault) foreach (var connection in config.Databases) connection.IsDefault = false;
                target.IsDefault = update.SetDefault;
            }
            if (update.ApplyEnableDangerousOperations) target.EnableDangerousOperations = update.EnableDangerousOperations;
            DatabaseConfigurationValidator.ThrowIfInvalid(config);
            write.Save(config);
            saved = true;
            var result = apply?.Invoke(config) ?? new(true, true, false, null, $"已更新数据库连接 '{target.Name}'。");
            return result with { ConfigPath = path, DatabaseName = target.Name, Database = CliConfigFileService.ToMaskedConnection(target) };
        }
        catch (Exception ex)
        {
            return new(false, saved, false, null, ex.Message)
            { ConfigPath = path, DatabaseName = saved ? target?.Name : name };
        }
    }
}
