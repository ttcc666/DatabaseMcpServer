using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DatabaseMcpServer.Gui.Avalonia;

/// <summary>
/// Windows tray icon that shows a native Win32 context menu.
/// Avalonia 11.3's TrayIcon.Menu uses a managed popup window, which appears as
/// an empty white box and dismisses the Win11 overflow flyout on right-click.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class Win32TrayIcon : IDisposable
{
    private const uint WmApp = 0x8000;
    private const uint WmTray = WmApp + 1;
    private const uint WmShowMenu = WmApp + 2;
    private const uint WmNull = 0x0000;
    private const uint WmDestroy = 0x0002;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmLButtonDblClk = 0x0203;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmContextMenu = 0x007B;
    private const uint NinSelect = 0x0400;
    private const uint NinKeySelect = 0x0401;

    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint NimSetVersion = 0x00000004;
    private const uint NotifyIconVersion4 = 4;

    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NifShowTip = 0x00000080;

    private const uint MfString = 0x00000000;
    private const uint MfPopup = 0x00000010;
    private const uint MfSeparator = 0x00000800;
    private const uint MfChecked = 0x00000008;

    private const uint TpmReturnCmd = 0x0100;
    private const uint TpmRightButton = 0x0002;

    private const uint WsPopup = 0x80000000;
    private const uint WsExToolWindow = 0x00000080;
    private const int ImageIcon = 1;
    private const uint LrLoadFromFile = 0x0010;
    private const int SmCxSmIcon = 49;
    private const int ErrorClassAlreadyExists = 1410;
    private const uint MsgfltAllow = 1;

    private readonly WndProc _wndProc;
    private readonly string _className;
    private readonly string _tooltip;
    private readonly uint _taskbarCreated;
    private IntPtr _hwnd;
    private IntPtr _hIcon;
    private bool _iconAdded;
    private bool _disposed;
    private bool _menuPosted;

    public event EventHandler? Clicked;

    public Func<IReadOnlyList<MenuItem>>? MenuFactory { get; set; }

    private Win32TrayIcon(string tooltip)
    {
        _tooltip = tooltip;
        _className = "DmsConfigManagerTray_" + Environment.ProcessId;
        _wndProc = WndProcCallback;
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    }

    public static Win32TrayIcon? TryCreate(string tooltip, Stream? iconStream)
    {
        var tray = new Win32TrayIcon(tooltip);
        try
        {
            tray.Initialize(iconStream);
            return tray;
        }
        catch
        {
            tray.Dispose();
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_hwnd != IntPtr.Zero)
        {
            RemoveNotifyIcon();
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
            UnregisterClass(_className, GetModuleHandle(null));
        }

        if (_hIcon != IntPtr.Zero)
        {
            DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }
    }

    private void Initialize(Stream? iconStream)
    {
        _hIcon = CreateIcon(iconStream);
        if (_hIcon == IntPtr.Zero)
        {
            throw new InvalidOperationException("Tray icon handle was not created.");
        }

        var wndClass = new WndClassEx
        {
            cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
            lpfnWndProc = _wndProc,
            hInstance = GetModuleHandle(null),
            lpszClassName = _className
        };

        var atom = RegisterClassEx(ref wndClass);
        if (atom == 0)
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorClassAlreadyExists)
            {
                throw new InvalidOperationException("RegisterClassEx failed: " + error);
            }
        }

        _hwnd = CreateWindowEx(
            WsExToolWindow,
            _className,
            "DmsConfigManagerTray",
            WsPopup,
            0,
            0,
            0,
            0,
            IntPtr.Zero,
            IntPtr.Zero,
            wndClass.hInstance,
            IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException("CreateWindowEx failed: " + Marshal.GetLastWin32Error());
        }

        if (_taskbarCreated != 0)
        {
            ChangeWindowMessageFilterEx(_hwnd, _taskbarCreated, MsgfltAllow, IntPtr.Zero);
        }

        AddNotifyIcon();
        if (!_iconAdded)
        {
            throw new InvalidOperationException("Shell_NotifyIcon NIM_ADD failed.");
        }
    }

    private void AddNotifyIcon()
    {
        var data = CreateNotifyIconData();
        if (Shell_NotifyIcon(_iconAdded ? NimModify : NimAdd, ref data))
        {
            _iconAdded = true;
            data.uVersion = NotifyIconVersion4;
            Shell_NotifyIcon(NimSetVersion, ref data);
        }
    }

    private void RemoveNotifyIcon()
    {
        if (!_iconAdded)
        {
            return;
        }

        var data = CreateNotifyIconData();
        Shell_NotifyIcon(NimDelete, ref data);
        _iconAdded = false;
    }

    private NotifyIconData CreateNotifyIconData()
    {
        return new NotifyIconData
        {
            cbSize = Marshal.SizeOf<NotifyIconData>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NifMessage | NifIcon | NifTip | NifShowTip,
            uCallbackMessage = WmTray,
            hIcon = _hIcon,
            szTip = _tooltip ?? string.Empty
        };
    }

    private IntPtr WndProcCallback(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == _taskbarCreated && _taskbarCreated != 0)
        {
            _iconAdded = false;
            AddNotifyIcon();
            return IntPtr.Zero;
        }

        if (msg == WmShowMenu)
        {
            _menuPosted = false;
            try
            {
                ShowContextMenu();
            }
            catch
            {
                // A tray-menu failure must not tear down the message window.
            }

            return IntPtr.Zero;
        }

        if (msg == WmTray)
        {
            var eventCode = (uint)(lParam.ToInt64() & 0xFFFF);
            switch (eventCode)
            {
                case WmLButtonUp:
                case WmLButtonDblClk:
                case NinSelect:
                case NinKeySelect:
                    Clicked?.Invoke(this, EventArgs.Empty);
                    break;
                case WmRButtonUp:
                case WmContextMenu:
                    if (!_menuPosted)
                    {
                        _menuPosted = true;
                        PostMessage(hWnd, WmShowMenu, IntPtr.Zero, IntPtr.Zero);
                    }

                    break;
            }

            return IntPtr.Zero;
        }

        if (msg == WmDestroy)
        {
            return IntPtr.Zero;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var items = MenuFactory?.Invoke();
        if (items == null || items.Count == 0)
        {
            return;
        }

        var actions = new Dictionary<uint, Action>();
        var hMenu = BuildMenu(items, actions);
        if (hMenu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            GetCursorPos(out var point);
            SetForegroundWindow(_hwnd);
            var selected = TrackPopupMenuEx(
                hMenu,
                TpmRightButton | TpmReturnCmd,
                point.X,
                point.Y,
                _hwnd,
                IntPtr.Zero);
            PostMessage(_hwnd, WmNull, IntPtr.Zero, IntPtr.Zero);
            if (selected != 0 && actions.TryGetValue(selected, out var action))
            {
                action();
            }
        }
        finally
        {
            DestroyMenu(hMenu);
        }
    }

    private static IntPtr BuildMenu(IReadOnlyList<MenuItem> items, Dictionary<uint, Action> actions)
    {
        var hMenu = CreatePopupMenu();
        if (hMenu == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        foreach (var item in items)
        {
            AppendItem(hMenu, item, actions);
        }

        return hMenu;
    }

    private static void AppendItem(IntPtr hMenu, MenuItem item, Dictionary<uint, Action> actions)
    {
        if (item.IsSeparator)
        {
            AppendMenu(hMenu, MfSeparator, UIntPtr.Zero, null);
            return;
        }

        if (item.Children is { Count: > 0 })
        {
            var submenu = BuildMenu(item.Children, actions);
            if (submenu != IntPtr.Zero)
            {
                AppendMenu(hMenu, MfString | MfPopup, (UIntPtr)submenu, item.Header);
            }

            return;
        }

        var id = (uint)(actions.Count + 1);
        if (item.Click != null)
        {
            actions[id] = item.Click;
        }

        var flags = MfString;
        if (item.IsChecked)
        {
            flags |= MfChecked;
        }

        AppendMenu(hMenu, flags, (UIntPtr)id, item.Header);
    }

    private static IntPtr CreateIcon(Stream? iconStream)
    {
        var fromProcess = ExtractSmallIconFromProcess();
        if (fromProcess != IntPtr.Zero)
        {
            return fromProcess;
        }

        if (iconStream == null)
        {
            return IntPtr.Zero;
        }

        var tempPath = Path.Combine(Path.GetTempPath(), "dms-config-manager-tray.ico");
        using (var file = File.Create(tempPath))
        {
            iconStream.CopyTo(file);
        }

        var size = GetSystemMetrics(SmCxSmIcon);
        if (size <= 0)
        {
            size = 16;
        }

        var handle = LoadImage(IntPtr.Zero, tempPath, ImageIcon, size, size, LrLoadFromFile);
        try
        {
            File.Delete(tempPath);
        }
        catch
        {
            // Best-effort cleanup of the extracted .ico.
        }

        return handle;
    }

    private static IntPtr ExtractSmallIconFromProcess()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return IntPtr.Zero;
        }

        var small = new IntPtr[1];
        var count = ExtractIconEx(processPath, 0, null, small, 1);
        return count > 0 ? small[0] : IntPtr.Zero;
    }

    internal sealed class MenuItem
    {
        public static MenuItem Separator { get; } = new() { IsSeparator = true };

        public string Header { get; init; } = string.Empty;
        public bool IsSeparator { get; init; }
        public bool IsChecked { get; init; }
        public Action? Click { get; init; }
        public IReadOnlyList<MenuItem>? Children { get; init; }
    }

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint cbSize;
        public uint style;
        public WndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WndClassEx lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point lpPoint);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(
        IntPtr hMenu,
        uint uFlags,
        int x,
        int y,
        IntPtr hwnd,
        IntPtr lptpm);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(
        IntPtr hInst,
        string name,
        int type,
        int cx,
        int cy,
        uint fuLoad);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    private static extern bool ChangeWindowMessageFilterEx(
        IntPtr hWnd,
        uint message,
        uint action,
        IntPtr pChangeFilterStruct);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NotifyIconData lpData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(
        string lpszFile,
        int nIconIndex,
        IntPtr[]? phiconLarge,
        IntPtr[]? phiconSmall,
        uint nIcons);
}
