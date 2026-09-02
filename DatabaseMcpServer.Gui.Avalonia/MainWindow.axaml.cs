using System;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DatabaseMcpServer.Gui.Avalonia.ViewModels;

namespace DatabaseMcpServer.Gui.Avalonia;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private bool _initialized;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
        Icon = IconAssets.LoadApplicationIcon();
        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;
        Opened += OnOpened;
        Closing += OnClosing;
        Closed += OnClosed;
        _viewModel.ChoosePathRequested += OnChoosePathRequested;
        _viewModel.ExternalFileChanged += OnExternalFileChanged;
        _viewModel.ConfirmRequested += OnConfirmRequested;
    }

    public MainWindowViewModel ViewModel => _viewModel;

    private void OnOpened(object? sender, EventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _viewModel.Initialize();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        // The window's close button hides the editor instead of terminating the
        // process. The tray menu is the explicit exit path.
        e.Cancel = true;
        Hide();
    }

    public void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    public void CloseFromApplication()
    {
        _allowClose = true;
        Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.ChoosePathRequested -= OnChoosePathRequested;
        _viewModel.ExternalFileChanged -= OnExternalFileChanged;
        _viewModel.ConfirmRequested -= OnConfirmRequested;
        _viewModel.Dispose();
    }

    private async Task<bool> OnConfirmRequested(string message)
    {
        var dialog = new ConfirmDialog(message);
        var result = await dialog.ShowDialog<bool>(this);
        if (!result)
        {
            DatabaseList.SelectedItem = _viewModel.SelectedDatabase;
        }

        return result;
    }

    private async void OnChoosePathRequested(object? sender, EventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "选择 databases.json 保存位置",
            SuggestedFileName = "databases.json",
            FileTypeChoices = [new FilePickerFileType("JSON 配置文件") { Patterns = ["*.json"] }]
        });
        if (file?.Path is { } path)
        {
            _viewModel.UseChosenPath(path.LocalPath);
        }
    }

    private void OnExternalFileChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(_viewModel.HandleExternalChange);
}
