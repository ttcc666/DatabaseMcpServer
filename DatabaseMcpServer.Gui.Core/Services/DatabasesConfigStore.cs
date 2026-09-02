using DatabaseMcpServer.Cli;
using DatabaseMcpServer.Models;

namespace DatabaseMcpServer.Gui.Core.Services;

public sealed class DatabasesConfigStore
{
    private readonly CliConfigFileService _fileService = new();
    public PathResolution? CurrentResolution { get; private set; }
    public string ActivePath { get; private set; } = string.Empty;
    public bool FileExists => !string.IsNullOrEmpty(ActivePath) && File.Exists(ActivePath);

    public void UseResolution(PathResolution resolution)
    {
        CurrentResolution = resolution;
        ActivePath = resolution.Path;
    }

    public void UseWritable(string path)
    {
        CurrentResolution = null;
        ActivePath = path;
    }

    public DatabasesConfig Load() => string.IsNullOrEmpty(ActivePath)
        ? throw new InvalidOperationException("尚未选择配置文件路径。")
        : _fileService.Load(ActivePath);

    public void Save(DatabasesConfig config)
    {
        if (string.IsNullOrEmpty(ActivePath)) throw new InvalidOperationException("尚未选择配置文件路径。");
        _fileService.Save(ActivePath, config);
    }
}
