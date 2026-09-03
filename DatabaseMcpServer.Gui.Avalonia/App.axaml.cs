using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DatabaseMcpServer.Gui.Avalonia.ViewModels;

namespace DatabaseMcpServer.Gui.Avalonia;

public partial class App : Application
{
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private MainWindow? _mainWindow;
    private TrayIcon? _trayIcon;
    private NativeMenu? _trayMenu;
    private Win32TrayIcon? _win32TrayIcon;
    private bool _trayMenuOpen;
    private bool _trayRefreshPending;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            _mainWindow = new MainWindow();
            desktop.MainWindow = _mainWindow;
            CreateTrayIcon();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void CreateTrayIcon()
    {
        if (OperatingSystem.IsWindows() && TryCreateWin32TrayIcon())
        {
            return;
        }

        CreateAvaloniaTrayIcon();
    }

    [SupportedOSPlatform("windows")]
    private bool TryCreateWin32TrayIcon()
    {
        using var iconStream = IconAssets.OpenTrayIcoStream();
        _win32TrayIcon = Win32TrayIcon.TryCreate("DMSConfigManager", iconStream);
        if (_win32TrayIcon == null)
        {
            return false;
        }

        _win32TrayIcon.Clicked += OnTrayIconClicked;
        _win32TrayIcon.MenuFactory = CreateWin32TrayMenu;
        return true;
    }

    [SupportedOSPlatform("windows")]
    private IReadOnlyList<Win32TrayIcon.MenuItem> CreateWin32TrayMenu()
    {
        var items = new List<Win32TrayIcon.MenuItem>
        {
            new()
            {
                Header = "打开窗口",
                Click = () => _mainWindow?.ShowFromTray()
            }
        };

        if (_mainWindow != null && _mainWindow.ViewModel.Databases.Count > 0)
        {
            items.Add(Win32TrayIcon.MenuItem.Separator);
            foreach (var database in _mainWindow.ViewModel.Databases)
            {
                var current = database;
                items.Add(new Win32TrayIcon.MenuItem
                {
                    Header = GetDatabaseMenuText(current),
                    Children =
                    [
                        new Win32TrayIcon.MenuItem
                        {
                            Header = "设为默认",
                            IsChecked = current.IsDefault,
                            Click = () => _mainWindow.ViewModel.SetDefaultFromTray(current)
                        },
                        new Win32TrayIcon.MenuItem
                        {
                            Header = "允许危险操作",
                            IsChecked = current.EnableDangerousOperations,
                            Click = () => _mainWindow.ViewModel.SetDangerousOperationsFromTray(
                                current,
                                !current.EnableDangerousOperations)
                        }
                    ]
                });
            }
        }

        items.Add(Win32TrayIcon.MenuItem.Separator);
        items.Add(new Win32TrayIcon.MenuItem
        {
            Header = "退出",
            Click = ExitApplication
        });
        return items;
    }

    private void CreateAvaloniaTrayIcon()
    {
        if (_mainWindow != null)
        {
            _mainWindow.ViewModel.Databases.CollectionChanged += OnDatabasesChanged;
        }

        _trayMenu = new NativeMenu();
        _trayMenu.Opening += OnTrayMenuOpening;
        _trayMenu.Closed += OnTrayMenuClosed;

        _trayIcon = new TrayIcon
        {
            ToolTipText = "DMSConfigManager",
            Icon = LoadTrayIcon(),
            Menu = _trayMenu,
            IsVisible = true
        };
        _trayIcon.Clicked += OnTrayIconClicked;

        var trayIcons = new TrayIcons();
        trayIcons.Add(_trayIcon);
        TrayIcon.SetIcons(this, trayIcons);
        RefreshTrayMenu();
    }

    private void OnTrayIconClicked(object? sender, EventArgs e) => _mainWindow?.ShowFromTray();

    private void OnTrayMenuOpening(object? sender, EventArgs e) => _trayMenuOpen = true;

    private void OnTrayMenuClosed(object? sender, EventArgs e)
    {
        _trayMenuOpen = false;
        if (!_trayRefreshPending)
        {
            return;
        }

        // Native menu implementations can invalidate the tray icon when its
        // item collection is rebuilt while the context menu is still open.
        Dispatcher.UIThread.Post(() =>
        {
            if (_trayMenuOpen)
            {
                return;
            }

            _trayRefreshPending = false;
            RefreshTrayMenu();
        });
    }

    private void OnDatabasesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (DatabaseItemViewModel database in e.OldItems)
            {
                database.PropertyChanged -= OnDatabasePropertyChanged;
            }
        }

        if (e.NewItems != null)
        {
            foreach (DatabaseItemViewModel database in e.NewItems)
            {
                database.PropertyChanged += OnDatabasePropertyChanged;
            }
        }

        RefreshTrayMenu();
    }

    private void OnDatabasePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DatabaseItemViewModel.Name)
            or nameof(DatabaseItemViewModel.Description)
            or nameof(DatabaseItemViewModel.ListTitle)
            or nameof(DatabaseItemViewModel.DbType)
            or nameof(DatabaseItemViewModel.IsDefault)
            or nameof(DatabaseItemViewModel.EnableDangerousOperations))
        {
            RefreshTrayMenu();
        }
    }

    private void RefreshTrayMenu()
    {
        if (_trayMenu == null || _mainWindow == null)
        {
            return;
        }

        if (_trayMenuOpen)
        {
            _trayRefreshPending = true;
            return;
        }

        _trayMenu.Items.Clear();
        _trayMenu.Items.Add(new NativeMenuItem("打开窗口")
        {
            Command = new RelayCommand(_mainWindow.ShowFromTray)
        });

        foreach (var database in _mainWindow.ViewModel.Databases)
        {
            var submenu = new NativeMenu();
            var setDefaultItem = new NativeMenuItem("设为默认")
            {
                ToggleType = NativeMenuItemToggleType.CheckBox,
                IsChecked = database.IsDefault
            };
            setDefaultItem.Click += (_, _) =>
            {
                _mainWindow.ViewModel.SetDefaultFromTray(database);
            };

            var dangerousItem = new NativeMenuItem("允许危险操作")
            {
                ToggleType = NativeMenuItemToggleType.CheckBox,
                IsChecked = database.EnableDangerousOperations
            };
            dangerousItem.Click += (_, _) =>
            {
                _mainWindow.ViewModel.SetDangerousOperationsFromTray(
                    database,
                    !database.EnableDangerousOperations);
            };

            submenu.Items.Add(setDefaultItem);
            submenu.Items.Add(dangerousItem);
            _trayMenu.Items.Add(new NativeMenuItem(GetDatabaseMenuText(database))
            {
                Menu = submenu
            });
        }

        _trayMenu.Items.Add(new NativeMenuItem("退出")
        {
            Command = new RelayCommand(ExitApplication)
        });
    }

    private static string GetDatabaseMenuText(DatabaseItemViewModel database)
    {
        var name = string.IsNullOrWhiteSpace(database.Name) ? "未命名连接" : database.Name;
        return database.IsDefault ? $"{name}（默认）" : name;
    }

    private void ExitApplication()
    {
        if (OperatingSystem.IsWindows())
        {
            DisposeWin32TrayIcon();
        }

        if (_trayIcon != null)
        {
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        if (_mainWindow != null)
        {
            _mainWindow.CloseFromApplication();
            _mainWindow.ViewModel.Databases.CollectionChanged -= OnDatabasesChanged;
            _mainWindow = null;
        }

        _desktop?.Shutdown();
    }

    [SupportedOSPlatform("windows")]
    private void DisposeWin32TrayIcon()
    {
        if (_win32TrayIcon == null)
        {
            return;
        }

        _win32TrayIcon.Clicked -= OnTrayIconClicked;
        _win32TrayIcon.Dispose();
        _win32TrayIcon = null;
    }

    private static WindowIcon? LoadTrayIcon() => IconAssets.LoadTrayIcon();
}

