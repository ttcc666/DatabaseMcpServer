using DatabaseMcpServer.Configuration;
using DatabaseMcpServer.Interfaces;
using DatabaseMcpServer.Models;

namespace DatabaseMcpServer.Services;

internal sealed class ConfigurationRuntimeApplier(ConfigurationFileStore files, Func<IDatabaseConfigService> resolveRuntime,
    Action startMonitor)
{
    public ConfigurationCommandResult Apply(string path, ConfigurationCommandResult result, bool allowEmptyInitialization = false)
    {
        if (!result.Success) return result;
        result.Details["saved"] = true;
        result.Details["runtimeApplied"] = false;
        try
        {
            if (files.Load(path).Databases.Count == 0)
            {
                result.Success = allowEmptyInitialization;
                result.Message = "配置已保存，尚无可用数据库连接。已有运行时将保留最近有效配置。";
                return result;
            }
            var runtime = resolveRuntime();
            var reload = runtime.ReloadConfiguration(followFileDefault: runtime.IsEnableMonitorConfigEnabled());
            result.Details["runtimeApplied"] = reload.RuntimeApplied || reload.Success;
            result.Details["selectionPersisted"] = reload.SelectionPersisted;
            result.Success = reload.Success;
            if (!reload.Success) result.Message = $"配置已保存，但未完成全部更新：{reload.Message}";
            startMonitor();
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"配置已保存，但未完成运行时更新：{ex.Message}";
        }
        return result;
    }
}
