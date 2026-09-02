using System;
using System.IO;
using System.Threading;

namespace DatabaseMcpServer.Gui.Core.Services;

/// <summary>
/// 监控单个文件的变更。
/// 内部以 250ms 去抖处理编辑器的 save-rename / temp-write 抖动,并避免 Created + Changed 双触发。
/// </summary>
public sealed class FileWatcherService : IDisposable
{
    private const int DebounceMilliseconds = 250;

    private readonly object _syncRoot = new();
    private FileSystemWatcher? _watcher;
    private System.Timers.Timer? _debounceTimer;
    private string? _watchedPath;
    private bool _suppressNextChange;
    private bool _disposed;

    public event EventHandler? FileChanged;

    public void Watch(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return;
        }

        Stop();

        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        lock (_syncRoot)
        {
            _watchedPath = fullPath;
            _watcher = new FileSystemWatcher(directory, Path.GetFileName(fullPath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
                IncludeSubdirectories = false,
                EnableRaisingEvents = false
            };
            _watcher.Changed += OnFileSystemEvent;
            _watcher.Created += OnFileSystemEvent;
            _watcher.Renamed += OnFileRenamed;

            _debounceTimer = new System.Timers.Timer(DebounceMilliseconds)
            {
                AutoReset = false,
                Enabled = false
            };
            _debounceTimer.Elapsed += OnDebounceElapsed;

            try
            {
                _watcher.EnableRaisingEvents = true;
            }
            catch
            {
                // 某些系统路径(网络盘/受限目录)会抛 FileSystemException,
                // 这里容忍:监控失败时主程序仍可正常工作,只是不会触发自动重载。
            }
        }
    }

    public void Stop()
    {
        lock (_syncRoot)
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Changed -= OnFileSystemEvent;
                _watcher.Created -= OnFileSystemEvent;
                _watcher.Renamed -= OnFileRenamed;
                _watcher.Dispose();
                _watcher = null;
            }

            if (_debounceTimer != null)
            {
                _debounceTimer.Stop();
                _debounceTimer.Elapsed -= OnDebounceElapsed;
                _debounceTimer.Dispose();
                _debounceTimer = null;
            }

            _watchedPath = null;
            _suppressNextChange = false;
        }
    }

    public void SuppressNextChange()
    {
        lock (_syncRoot)
        {
            _suppressNextChange = true;
        }
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        ScheduleFire(e.FullPath);
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        ScheduleFire(e.FullPath);
    }

    private void ScheduleFire(string fullPath)
    {
        if (_disposed)
        {
            return;
        }

        var watchedPath = _watchedPath;
        if (string.IsNullOrEmpty(watchedPath))
        {
            return;
        }

        if (!string.Equals(Path.GetFileName(fullPath), Path.GetFileName(watchedPath), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (_syncRoot)
        {
            if (_debounceTimer == null)
            {
                return;
            }

            _debounceTimer.Stop();
            _debounceTimer.Start();
        }
    }

    private void OnDebounceElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        bool suppress;
        lock (_syncRoot)
        {
            suppress = _suppressNextChange;
            if (_suppressNextChange)
            {
                _suppressNextChange = false;
            }
        }

        if (suppress)
        {
            return;
        }

        try
        {
            FileChanged?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // 订阅者异常不应让 watcher 崩溃。
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        FileChanged = null;
    }
}


