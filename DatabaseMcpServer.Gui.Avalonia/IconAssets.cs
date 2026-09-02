using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace DatabaseMcpServer.Gui.Avalonia;

internal static class IconAssets
{
    private const string ApplicationIconPath =
        "avares://DatabaseMcpServer.Gui.Avalonia/icons/dms-config-manager.png";
    private const string TrayIconPath =
        "avares://DatabaseMcpServer.Gui.Avalonia/icons/dms-config-manager-tray.png";

    private const string TrayIcoPath =
        "avares://DatabaseMcpServer.Gui.Avalonia/icons/dms-config-manager.ico";

    public static WindowIcon? LoadApplicationIcon() => Load(ApplicationIconPath);

    public static WindowIcon? LoadTrayIcon() => Load(TrayIconPath);

    public static Stream? OpenTrayIcoStream()
    {
        try
        {
            return AssetLoader.Open(new Uri(TrayIcoPath));
        }
        catch
        {
            return null;
        }
    }

    private static WindowIcon? Load(string path)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri(path));
            return new WindowIcon(new Bitmap(stream));
        }
        catch
        {
            // An unavailable icon must not prevent the configuration editor from opening.
            return null;
        }
    }
}
