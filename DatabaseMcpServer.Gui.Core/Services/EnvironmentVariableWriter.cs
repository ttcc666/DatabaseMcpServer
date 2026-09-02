using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DatabaseMcpServer.Gui.Core.Services;

public interface IEnvironmentVariableWriter
{
    void SetUserEnvironmentVariable(string name, string value);
    void RemoveUserEnvironmentVariable(string name);
    string? GetUserEnvironmentVariable(string name);
}

public sealed class EnvironmentVariableWriter : IEnvironmentVariableWriter
{
    private const int WmSettingChange = 0x001A;
    private const int SmtoAbortIfHung = 0x0002;
    private const int HwndBroadcast = 0xffff;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        string lParam,
        uint flags,
        uint timeout,
        IntPtr result);

    public void SetUserEnvironmentVariable(string name, string value)
    {
        ValidateName(name);
        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Process);

        if (!OperatingSystem.IsWindows()) return;

        using var key = Registry.CurrentUser.OpenSubKey(@"Environment", writable: true)
            ?? throw new InvalidOperationException("无法打开用户环境变量存储。");
        key.SetValue(name, value, RegistryValueKind.ExpandString);
        BroadcastSettingChange();
    }

    public void RemoveUserEnvironmentVariable(string name)
    {
        ValidateName(name);
        Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.Process);

        if (!OperatingSystem.IsWindows()) return;

        using var key = Registry.CurrentUser.OpenSubKey(@"Environment", writable: true)
            ?? throw new InvalidOperationException("无法打开用户环境变量存储。");
        key.DeleteValue(name, throwOnMissingValue: false);
        BroadcastSettingChange();
    }

    public string? GetUserEnvironmentVariable(string name)
    {
        ValidateName(name);
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Environment", writable: false);
            return key?.GetValue(name) as string ?? Environment.GetEnvironmentVariable(name);
        }

        return Environment.GetEnvironmentVariable(name);
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("环境变量名不能为空。", nameof(name));
        }
    }

    private static void BroadcastSettingChange()
    {
        try
        {
            SendMessageTimeout(
                (IntPtr)HwndBroadcast,
                WmSettingChange,
                IntPtr.Zero,
                "Environment",
                SmtoAbortIfHung,
                5000,
                IntPtr.Zero);
        }
        catch
        {
            // 环境变量已写入；广播失败只影响已运行进程的即时感知。
        }
    }
}
