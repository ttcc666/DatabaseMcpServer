using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using DatabaseMcpServer.Gui.Core.Services;
using DatabaseMcpServer.Models;

namespace DatabaseMcpServer.Gui.Avalonia.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;
    public RelayCommand(Action execute, Func<bool>? canExecute = null) { _execute = execute; _canExecute = canExecute; }
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => _execute();
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    public RelayCommand(Action<T?> execute) => _execute = execute;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _execute(parameter is T value ? value : default);
}

public sealed class ConnectionFieldViewModel : ObservableObject
{
    private string _value;
    private readonly Action _changed;
    public ConnectionFieldViewModel(DbTypeConnectionFields.Field field, string? value, Action changed)
    {
        Field = field;
        _value = value ?? field.DefaultValue ?? string.Empty;
        _changed = changed;
    }
    public DbTypeConnectionFields.Field Field { get; }
    public string Key => Field.Key;
    public string Label => Field.Label;
    public bool IsPassword => Field.IsPassword;
    public string Value { get => _value; set { if (SetProperty(ref _value, value ?? string.Empty)) _changed(); } }
}

public sealed class SettingRowViewModel : ObservableObject
{
    private readonly IReadOnlyList<DbTypeConnectionFields.Field> _definitions;
    private readonly Action _changed;
    private string _key;
    private string _value;

    public SettingRowViewModel(
        string? key,
        string? value,
        IEnumerable<DbTypeConnectionFields.Field>? definitions,
        Action changed,
        Action remove)
    {
        _definitions = definitions?.ToArray() ?? Array.Empty<DbTypeConnectionFields.Field>();
        _key = key ?? string.Empty;
        var defaultValue = FindDefinition(_key)?.DefaultValue;
        _value = string.IsNullOrWhiteSpace(value) ? defaultValue ?? string.Empty : value;
        _changed = changed;
        RemoveCommand = new RelayCommand(remove);
    }

    public string Key
    {
        get => _key;
        set => SetKey(value);
    }

    // ComboBox.SelectedItem can briefly report null while its item container is
    // refreshed. Keep the current selection in that transient state, otherwise
    // the binding would clear the key and the documented default value.
    public string? SelectedKey
    {
        get => string.IsNullOrEmpty(_key) ? null : _key;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            SetKey(value);
        }
    }

    private void SetKey(string? value)
    {
        var key = value ?? string.Empty;
        if (!SetProperty(ref _key, key, nameof(Key))) return;

        var defaultValue = FindDefinition(key)?.DefaultValue;
        _value = defaultValue ?? string.Empty;
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(SelectedKey));
        _changed();
    }

    public string Value
    {
        get => _value;
        set
        {
            if (!SetProperty(ref _value, value ?? string.Empty)) return;
            _changed();
        }
    }

    public IReadOnlyList<string> KeyOptions =>
        _definitions.Select(x => x.Key)
            .Concat(string.IsNullOrWhiteSpace(_key) ? Enumerable.Empty<string>() : [_key])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public ICommand RemoveCommand { get; }

    private DbTypeConnectionFields.Field? FindDefinition(string key) =>
        _definitions.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
}

public sealed class DatabaseItemViewModel : ObservableObject
{
    private readonly DatabaseConnection _model;
    private string _name;
    private string _dbType;
    private string _description;
    private bool _isDefault;
    private bool _allowDangerousOperations;
    private bool _hasStructuredFields;
    private bool _isEditing;
    private bool _isDraft;

    public DatabaseItemViewModel(DatabaseConnection model)
    {
        _model = model;
        _name = model.Name;
        var originalDbType = model.DbType;
        var normalizedDbType = DatabaseTypeCatalog.Normalize(originalDbType);
        _dbType = string.IsNullOrWhiteSpace(normalizedDbType)
            ? DatabaseTypeCatalog.InferFromConnectionString(model.ConnectionString)
            : normalizedDbType;
        _model.DbType = _dbType;
        WasDbTypeRecovered = !string.Equals(originalDbType, _dbType, StringComparison.Ordinal);
        _description = model.Description ?? string.Empty;
        _isDefault = model.IsDefault;
        _allowDangerousOperations = model.AllowDangerousOperations;
        ConnectionFields = new();
        PerformanceSettings = new();
        OptionalSettings = new();
        OptimizationSettings = new();
        PerformanceSettings.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasPerformanceSettings));
        OptionalSettings.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasOptionalSettings));
        OptimizationSettings.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasOptimizationSettings));
        RebuildEditorFields();
        AddPerformanceCommand = new RelayCommand(AddPerformance);
        AddOptionalCommand = new RelayCommand(AddOptional);
        AddOptimizationCommand = new RelayCommand(AddOptimization);
    }

    /// <summary>
    /// Indicates that a legacy value was normalized, or that a missing dbType was recovered from the connection string.
    /// </summary>
    public bool WasDbTypeRecovered { get; }

    public string Name
    {
        get => _name;
        set
        {
            if (!SetProperty(ref _name, value ?? string.Empty)) return;
            _model.Name = _name;
            OnPropertyChanged(nameof(ListTitle));
            Changed?.Invoke();
        }
    }
    public string ListTitle => string.IsNullOrWhiteSpace(_description)
        ? _name
        : $"{_name} · {_description}";
    public string DbIconFallback => _dbType.ToLowerInvariant() switch
    {
        "mysql" or "polardb" or "tidb" or "oceanbase" => "🐬",
        "postgresql" or "opengauss" or "gaussdb" or "gaussdbnative" or "hg" or "vastbase" or "kdbndp" or "kingbase" => "🐘",
        "mongodb" => "🍃",
        "oracle" or "oceanbasefororacle" => "◆",
        "sqlite" or "duckdb" => "▤",
        "sqlserver" => "▣",
        "clickhouse" or "doris" => "◈",
        _ => "▦"
    };
    private static readonly IReadOnlyDictionary<string, string> DatabaseIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["mysql"] = "mysql.svg", ["postgresql"] = "postgres.svg", ["sqlserver"] = "sqlserver.svg",
        ["sqlite"] = "sqlite.svg", ["oracle"] = "oracle.svg", ["mongodb"] = "mongodb.svg",
        ["questdb"] = "questdb.svg", ["tdengine"] = "tdengine.svg", ["duckdb"] = "duckdb.svg",
        ["doris"] = "doris.svg", ["dm"] = "dm.svg", ["kdbndp"] = "kingbase.svg",
        ["kingbase"] = "kingbase.svg", ["oscar"] = "oscar.png", ["hg"] = "highgo.png",
        ["vastbase"] = "vastbase.svg", ["goldendb"] = "goldendb.png", ["gbase"] = "gbase.png",
        ["oceanbase"] = "oceanbase.svg", ["oceanbasefororacle"] = "oceanbase.svg", ["tidb"] = "tidb.svg",
        ["polardb"] = "polardb.webp", ["clickhouse"] = "clickhouse.svg", ["opengauss"] = "opengauss.svg",
        ["gaussdb"] = "gaussdb.svg", ["gaussdbnative"] = "gaussdb.svg"
    };
    public string? IconFile => DatabaseIcons.TryGetValue(_dbType, out var file) ? file : null;
    public string? IconPath => IconFile is null ? null : $"avares://DatabaseMcpServer.Gui.Avalonia/icons/{IconFile}";
    public bool IconIsSvg => IconFile?.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) == true;
    public bool IconIsRaster => IconFile is not null && !IconIsSvg;
    public bool ShowIconFallback => IconFile is null;
    public string DbType
    {
        get => _dbType;
        set
        {
            var normalized = DatabaseTypeCatalog.Normalize(value);
            // A ComboBox can report null briefly while it refreshes its items. Never
            // turn an existing, valid database type into an empty string.
            if (string.IsNullOrWhiteSpace(normalized) && !string.IsNullOrWhiteSpace(_dbType)) return;
            if (!SetProperty(ref _dbType, normalized)) return;
            _model.DbType = _dbType;
            RebuildEditorFields();
            OnPropertyChanged(nameof(DbIconFallback));
            OnPropertyChanged(nameof(IconFile));
            OnPropertyChanged(nameof(IconPath));
            OnPropertyChanged(nameof(IconIsSvg));
            OnPropertyChanged(nameof(IconIsRaster));
            OnPropertyChanged(nameof(ShowIconFallback));
            OnPropertyChanged(nameof(PerformanceKeys));
            OnPropertyChanged(nameof(OptionalKeys));
            OnPropertyChanged(nameof(OptimizationKeys));
            OnPropertyChanged(nameof(HasPerformanceCatalog));
            OnPropertyChanged(nameof(HasOptionalCatalog));
            OnPropertyChanged(nameof(HasOptimizationCatalog));
            Changed?.Invoke();
        }
    }
    public string ConnectionString { get => _model.ConnectionString; set { if (_model.ConnectionString != value) { _model.ConnectionString = value ?? string.Empty; OnPropertyChanged(); Changed?.Invoke(); } } }
    public string Description
    {
        get => _description;
        set
        {
            if (!SetProperty(ref _description, value ?? string.Empty)) return;
            _model.Description = string.IsNullOrWhiteSpace(_description) ? null : _description;
            OnPropertyChanged(nameof(ListTitle));
            Changed?.Invoke();
        }
    }
    public bool IsDefault { get => _isDefault; set { if (SetProperty(ref _isDefault, value)) { _model.IsDefault = value; Changed?.Invoke(); } } }
    public bool AllowDangerousOperations { get => _allowDangerousOperations; set { if (SetProperty(ref _allowDangerousOperations, value)) { _model.AllowDangerousOperations = value; Changed?.Invoke(); } } }
    public bool HasStructuredFields { get => _hasStructuredFields; private set => SetProperty(ref _hasStructuredFields, value); }
    public bool HasRawConnection => !HasStructuredFields;
    public bool CanEdit => IsEditing;
    public bool IsEditing { get => _isEditing; set { if (SetProperty(ref _isEditing, value)) OnPropertyChanged(nameof(CanEdit)); } }
    public bool IsDraft { get => _isDraft; set => SetProperty(ref _isDraft, value); }
    public bool HasCompleteRequiredParameters =>
        !HasStructuredFields || ConnectionFields.All(field => !string.IsNullOrWhiteSpace(field.Value));
    public ObservableCollection<ConnectionFieldViewModel> ConnectionFields { get; }
    public ObservableCollection<SettingRowViewModel> PerformanceSettings { get; }
    public bool HasPerformanceSettings => PerformanceSettings.Count > 0;
    public bool HasPerformanceCatalog => DbTypeConnectionFields.GetPerformanceFields(_dbType).Count > 0;
    public IReadOnlyList<string> PerformanceKeys => DbTypeConnectionFields.GetPerformanceFields(_dbType).Select(x => x.Key).ToArray();
    public ObservableCollection<SettingRowViewModel> OptionalSettings { get; }
    public bool HasOptionalSettings => OptionalSettings.Count > 0;
    public bool HasOptionalCatalog => DbTypeConnectionFields.GetOptionalFields(_dbType).Count > 0;
    public IReadOnlyList<string> OptionalKeys => DbTypeConnectionFields.GetOptionalFields(_dbType).Select(x => x.Key).ToArray();
    public IReadOnlyList<string> OptimizationKeys => DbTypeConnectionFields.GetOptimizationFields(_dbType).Select(x => x.Key).ToArray();
    public ObservableCollection<SettingRowViewModel> OptimizationSettings { get; }
    public bool HasOptimizationSettings => OptimizationSettings.Count > 0;
    public bool HasOptimizationCatalog => DbTypeConnectionFields.GetOptimizationFields(_dbType).Count > 0;
    public ICommand AddPerformanceCommand { get; }
    public ICommand AddOptionalCommand { get; }
    public ICommand AddOptimizationCommand { get; }
    public event Action? Changed;

    public DatabaseConnection ToModel()
    {
        _model.DbType = DatabaseTypeCatalog.Normalize(_dbType);
        _model.ConnectionString = BuildConnectionString();
        _model.OptimizationSettings = ToDictionary(OptimizationSettings);
        return _model;
    }

    private void RebuildEditorFields()
    {
        var parsed = DbTypeConnectionFields.Parse(_dbType, _model.ConnectionString);
        ConnectionFields.Clear();
        var fields = DbTypeConnectionFields.GetFields(_dbType);
        HasStructuredFields = fields.Count > 0;
        OnPropertyChanged(nameof(HasRawConnection));
        foreach (var field in fields)
        {
            ConnectionFields.Add(new ConnectionFieldViewModel(field, DbTypeConnectionFields.GetValue(parsed, field.Key), OnEditorChanged));
        }
        PerformanceSettings.Clear();
        var performanceDefinitions = DbTypeConnectionFields.GetPerformanceFields(_dbType);
        foreach (var pair in parsed.Where(pair => DbTypeConnectionFields.IsPerformanceKey(_dbType, pair.Key)))
        {
            SettingRowViewModel? row = null;
            row = new SettingRowViewModel(pair.Key, pair.Value, performanceDefinitions, OnEditorChanged, () => RemovePerformance(row));
            PerformanceSettings.Add(row);
        }
        OptionalSettings.Clear();
        var optionalDefinitions = DbTypeConnectionFields.GetOptionalFields(_dbType);
        foreach (var pair in parsed.Where(pair => DbTypeConnectionFields.IsOptionalKey(_dbType, pair.Key)))
        {
            SettingRowViewModel? row = null;
            row = new SettingRowViewModel(pair.Key, pair.Value, optionalDefinitions, OnEditorChanged, () => RemoveOptional(row));
            OptionalSettings.Add(row);
        }
        OptimizationSettings.Clear();
        var optimizationDefinitions = DbTypeConnectionFields.GetOptimizationFields(_dbType);
        foreach (var pair in _model.OptimizationSettings ?? new Dictionary<string, string>())
        {
            SettingRowViewModel? row = null;
            row = new SettingRowViewModel(pair.Key, pair.Value, optimizationDefinitions, OnEditorChanged, () => RemoveOptimization(row));
            OptimizationSettings.Add(row);
        }
    }

    private string BuildConnectionString()
    {
        if (!HasStructuredFields) return _model.ConnectionString;
        var values = ConnectionFields.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var row in PerformanceSettings.Where(x => !string.IsNullOrWhiteSpace(x.Key))) values[row.Key] = row.Value;
        foreach (var row in OptionalSettings.Where(x => !string.IsNullOrWhiteSpace(x.Key))) values[row.Key] = row.Value;
        return DbTypeConnectionFields.Assemble(_dbType, values);
    }

    private void OnEditorChanged() { _model.ConnectionString = BuildConnectionString(); Changed?.Invoke(); }
    private void AddPerformance()
    {
        SettingRowViewModel? row = null;
        row = new SettingRowViewModel(string.Empty, string.Empty, DbTypeConnectionFields.GetPerformanceFields(_dbType), OnEditorChanged, () => RemovePerformance(row));
        PerformanceSettings.Add(row);
        Changed?.Invoke();
    }
    private void RemovePerformance(SettingRowViewModel? row) { if (row != null) { PerformanceSettings.Remove(row); Changed?.Invoke(); } }
    private void AddOptional()
    {
        SettingRowViewModel? row = null;
        row = new SettingRowViewModel(string.Empty, string.Empty, DbTypeConnectionFields.GetOptionalFields(_dbType), OnEditorChanged, () => RemoveOptional(row));
        OptionalSettings.Add(row);
        Changed?.Invoke();
    }
    private void RemoveOptional(SettingRowViewModel? row) { if (row != null) { OptionalSettings.Remove(row); Changed?.Invoke(); } }
    private void AddOptimization()
    {
        SettingRowViewModel? row = null;
        row = new SettingRowViewModel(string.Empty, string.Empty, DbTypeConnectionFields.GetOptimizationFields(_dbType), OnEditorChanged, () => RemoveOptimization(row));
        OptimizationSettings.Add(row);
        Changed?.Invoke();
    }
    private void RemoveOptimization(SettingRowViewModel? row) { if (row != null) { OptimizationSettings.Remove(row); Changed?.Invoke(); } }
    private static Dictionary<string, string>? ToDictionary(IEnumerable<SettingRowViewModel> rows)
    {
        var result = rows.Where(x => !string.IsNullOrWhiteSpace(x.Key)).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        return result.Count == 0 ? null : result;
    }
}

public sealed record ConfigSourceOption(PathSource Source, string Label)
{
    public override string ToString() => Label;
}

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly ConfigPathService _pathService = new();
    private readonly DatabasesConfigStore _store = new();
    private readonly EnvironmentVariableWriter _environmentVariableWriter = new();
    private readonly FileWatcherService _watcher = new();
    private readonly McpRuntimeActivator _runtimeActivator = new();
    private DatabaseItemViewModel? _selectedDatabase;
    private ConfigSourceOption _selectedConfigSource;
    private string _currentPath = "未选择配置文件";
    private string _statusText = "就绪";
    private bool _isDirty;
    private bool _isBusy;
    private bool _isEditing;
    private bool _updatingConfigurationSource;
    private bool _sourceConfigurationDirty;
    private DatabaseConnection? _editSnapshot;
    private DatabaseItemViewModel? _selectionBeforeNew;
    private bool _editingNewDatabase;
    private bool _dirtyBeforeEdit;
    private bool _normalizingDefaults;
    private bool _suppressItemDrafts;
    private bool _selectionPromptInFlight;
    private readonly Dictionary<DatabaseItemViewModel, Action> _databaseChangedHandlers = new();

    public MainWindowViewModel()
    {
        Databases = new();
        ConfigSources =
        [
            new ConfigSourceOption(PathSource.EnvironmentVariable, "环境变量"),
            new ConfigSourceOption(PathSource.UserProfile, "用户目录")
        ];
        _selectedConfigSource = ConfigSources[1];

        NewCommand = new RelayCommand(NewDatabase);
        EditCommand = new RelayCommand(BeginEdit, () => SelectedDatabase != null && !IsEditing);
        DoneCommand = new RelayCommand(DoneEditing, () => SelectedDatabase != null && IsEditing);
        CancelCommand = new RelayCommand(CancelEditing, () => SelectedDatabase != null && IsEditing);
        DeleteCommand = new RelayCommand(DeleteDatabase, () => SelectedDatabase != null);
        SetDefaultCommand = new RelayCommand(SetDefault, () => SelectedDatabase != null);
        ReloadCommand = new RelayCommand(ReloadCurrentPath);
        SaveCommand = new RelayCommand(Save, () => !IsBusy && IsDirty);
        TestConnectionCommand = new RelayCommand(async () => await TestConnectionAsync(), () => SelectedDatabase != null && !IsBusy);
        ChoosePathCommand = new RelayCommand(
            () => ChoosePathRequested?.Invoke(this, EventArgs.Empty),
            () => IsEnvironmentSource);
        _watcher.FileChanged += (_, _) => ExternalFileChanged?.Invoke(this, EventArgs.Empty);
    }

    public ObservableCollection<DatabaseItemViewModel> Databases { get; }
    public IReadOnlyList<ConfigSourceOption> ConfigSources { get; }
    public IReadOnlyList<string> DbTypes => DatabaseTypeCatalog.All;

    public ConfigSourceOption SelectedConfigSource
    {
        get => _selectedConfigSource;
        set
        {
            if (value == null || !SetProperty(ref _selectedConfigSource, value)) return;

            OnPropertyChanged(nameof(IsEnvironmentSource));
            OnPropertyChanged(nameof(IsPathReadOnly));
            ChoosePathCommand.RaiseCanExecuteChanged();
            if (!_updatingConfigurationSource)
            {
                ChangeConfigurationSource(value.Source);
            }
        }
    }

    public DatabaseItemViewModel? SelectedDatabase
    {
        get => _selectedDatabase;
        set
        {
            if (ReferenceEquals(_selectedDatabase, value))
            {
                return;
            }

            if (_selectionPromptInFlight)
            {
                Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(SelectedDatabase)));
                return;
            }

            if (IsEditing && _selectedDatabase != null && value != null)
            {
                if (!HasUnstashedEdits())
                {
                    IsEditing = false;
                    ClearEditSession();
                    ApplySelection(value);
                    return;
                }

                _selectionPromptInFlight = true;
                Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(SelectedDatabase)));
                _ = ConfirmThenSelectAsync(value);
                return;
            }

            if (IsEditing && value == null)
            {
                Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(SelectedDatabase)));
                return;
            }

            ApplySelection(value);
        }
    }

    public string CurrentPath
    {
        get => _currentPath;
        set
        {
            if (!SetProperty(ref _currentPath, value ?? string.Empty) || _updatingConfigurationSource) return;
            _sourceConfigurationDirty = true;
            IsDirty = true;
            StatusText = "配置文件位置已修改，点击保存后生效。";
        }
    }

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public bool IsEnvironmentSource => SelectedConfigSource.Source == PathSource.EnvironmentVariable;
    public bool IsPathReadOnly => !IsEnvironmentSource;
    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (SetProperty(ref _isDirty, value)) SaveCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                SaveCommand.RaiseCanExecuteChanged();
                TestConnectionCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (!SetProperty(ref _isEditing, value)) return;
            if (SelectedDatabase != null) SelectedDatabase.IsEditing = value;
            EditCommand.RaiseCanExecuteChanged();
            DoneCommand.RaiseCanExecuteChanged();
            CancelCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasSelection => SelectedDatabase != null;
    public ICommand NewCommand { get; }
    public RelayCommand EditCommand { get; }
    public RelayCommand DoneCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand SetDefaultCommand { get; }
    public RelayCommand ReloadCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand TestConnectionCommand { get; }
    public RelayCommand ChoosePathCommand { get; }
    public event EventHandler? ChoosePathRequested;
    public event EventHandler? ExternalFileChanged;
    public event Func<string, Task<bool>>? ConfirmRequested;

    public bool CanSaveToDisk => Databases.All(item => item.HasCompleteRequiredParameters);

    public void Initialize()
    {
        var environmentPath = _environmentVariableWriter.GetUserEnvironmentVariable(ConfigPathService.EnvironmentVariableName);
        var source = string.IsNullOrWhiteSpace(environmentPath)
            ? PathSource.UserProfile
            : PathSource.EnvironmentVariable;
        var option = ConfigSources.First(x => x.Source == source);

        _updatingConfigurationSource = true;
        SelectedConfigSource = option;
        _updatingConfigurationSource = false;

        var resolution = _pathService.CreateResolution(source, environmentPath);
        SetCurrentPath(resolution.Path);
        _store.UseResolution(resolution);
        _sourceConfigurationDirty = false;
        LoadConfig();
    }

    public void UseChosenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !IsEnvironmentSource) return;
        CurrentPath = path;
    }

    private void ChangeConfigurationSource(PathSource source)
    {
        var environmentPath = source == PathSource.EnvironmentVariable
            ? _environmentVariableWriter.GetUserEnvironmentVariable(ConfigPathService.EnvironmentVariableName)
            : null;
        var resolution = _pathService.CreateResolution(source, environmentPath);

        SetCurrentPath(resolution.Path);
        _store.UseResolution(resolution);
        if (resolution.ExistedAtStartup)
        {
            LoadConfig();
        }
        else
        {
            _watcher.Watch(resolution.Path);
        }

        _sourceConfigurationDirty = true;
        IsDirty = true;
        StatusText = resolution.ExistedAtStartup
            ? $"已加载 {Databases.Count} 个数据库连接，点击保存后切换配置来源。"
            : source == PathSource.EnvironmentVariable
                ? $"将使用环境变量 {ConfigPathService.EnvironmentVariableName}，点击保存后生成配置文件。"
                : "用户目录中尚无配置文件，点击保存后生成。";
    }

    private void ReloadCurrentPath()
    {
        try
        {
            var path = IsEnvironmentSource
                ? _pathService.NormalizePath(CurrentPath)
                : _pathService.UserProfilePath;
            var resolution = _pathService.CreateResolution(SelectedConfigSource.Source, path);
            SetCurrentPath(resolution.Path);
            _store.UseResolution(resolution);
            LoadConfig();
        }
        catch (Exception ex)
        {
            StatusText = $"刷新失败：{ex.Message}";
        }
    }

    private void LoadConfig()
    {
        try
        {
            var config = _store.FileExists ? _store.Load() : new DatabasesConfig();
            var repairedDbTypes = 0;
            var defaultConnections = config.Databases.Where(x => x.IsDefault).ToArray();
            var repairedDefaults = defaultConnections.Length > 1;
            var latestDefault = defaultConnections.LastOrDefault();
            if (repairedDefaults)
            {
                // If an older file contains multiple defaults, the last one wins.
                // The normalized value is written when the user next saves.
                foreach (var connection in config.Databases)
                {
                    connection.IsDefault = ReferenceEquals(connection, latestDefault);
                }
            }

            foreach (var item in Databases)
            {
                DetachDatabase(item);
            }

            Databases.Clear();
            _suppressItemDrafts = true;
            try
            {
                foreach (var connection in config.Databases)
                {
                    var item = new DatabaseItemViewModel(connection);
                    if (item.WasDbTypeRecovered) repairedDbTypes++;
                    AttachDatabase(item);
                    Databases.Add(item);
                    item.IsDraft = item.WasDbTypeRecovered
                        || (repairedDefaults
                            && defaultConnections.Contains(connection)
                            && !ReferenceEquals(connection, latestDefault));
                }
            }
            finally
            {
                _suppressItemDrafts = false;
            }

            ApplySelection(Databases.FirstOrDefault());
            IsEditing = false;
            ClearEditSession();
            _watcher.Watch(_store.ActivePath);
            IsDirty = _sourceConfigurationDirty || !_store.FileExists || repairedDbTypes > 0 || repairedDefaults;
            StatusText = _store.FileExists
                ? repairedDbTypes > 0
                    ? $"已加载 {Databases.Count} 个数据库连接，并修复 {repairedDbTypes} 个数据库类型；点击保存后写回标准值。"
                    : repairedDefaults
                        ? $"已加载 {Databases.Count} 个数据库连接，并保留最后一个默认连接；点击保存后写回。"
                        : $"已加载 {Databases.Count} 个数据库连接。"
                : "配置文件尚未创建，点击保存后生成。";
        }
        catch (Exception ex)
        {
            Databases.Clear();
            ApplySelection(null);
            StatusText = $"加载失败：{ex.Message}";
        }
    }

    private void NewDatabase()
    {
        _dirtyBeforeEdit = IsDirty;
        _selectionBeforeNew = SelectedDatabase;
        _editSnapshot = null;
        _editingNewDatabase = true;

        var item = new DatabaseItemViewModel(new DatabaseConnection
        {
            Name = $"database-{Databases.Count + 1}",
            DbType = DatabaseTypeCatalog.All[0]
        });
        AttachDatabase(item);
        Databases.Add(item);
        item.IsDraft = true;
        ApplySelection(item);
        IsEditing = true;
        MarkDirty();
    }

    private void BeginEdit()
    {
        if (SelectedDatabase == null) return;

        _dirtyBeforeEdit = IsDirty;
        _editSnapshot = CloneDatabase(SelectedDatabase.ToModel());
        _selectionBeforeNew = null;
        _editingNewDatabase = false;
        IsEditing = true;
    }

    private void DoneEditing() => StashItem(SelectedDatabase);

    private void StashItem(DatabaseItemViewModel? item)
    {
        if (item == null)
        {
            return;
        }

        item.IsDraft = true;
        IsEditing = false;
        ClearEditSession();
        SaveCommand.RaiseCanExecuteChanged();
        StatusText = item.HasCompleteRequiredParameters
            ? "已暂存当前连接。"
            : "已暂存当前连接。必须参数未填完整，保存前需补全。";
    }

    private void ApplySelection(DatabaseItemViewModel? value)
    {
        if (ReferenceEquals(_selectedDatabase, value))
        {
            return;
        }

        _suppressItemDrafts = true;
        try
        {
            _selectedDatabase = value;
            // SetProperty would publish CallerMemberName "ApplySelection", which
            // does not refresh SelectedDatabase bindings on the details pane.
            OnPropertyChanged(nameof(SelectedDatabase));
        }
        finally
        {
            _suppressItemDrafts = false;
        }

        IsEditing = false;
        RaiseCommands();
    }

    private async Task ConfirmThenSelectAsync(DatabaseItemViewModel pending)
    {
        var toStash = _selectedDatabase;
        try
        {
            var confirmed = ConfirmRequested == null
                || await ConfirmRequested("当前配置已修改，是否暂存并切换到其他连接？");
            if (!confirmed)
            {
                OnPropertyChanged(nameof(SelectedDatabase));
                Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(SelectedDatabase)));
                return;
            }

            StashItem(toStash);
            ApplySelection(pending);
        }
        finally
        {
            _selectionPromptInFlight = false;
        }
    }

    private void CancelEditing()
    {
        if (SelectedDatabase == null || !IsEditing) return;

        var item = SelectedDatabase;
        var wasDirty = _dirtyBeforeEdit;
        IsEditing = false;

        if (_editingNewDatabase)
        {
            DetachDatabase(item);
            Databases.Remove(item);
            ApplySelection(_selectionBeforeNew != null && Databases.Contains(_selectionBeforeNew)
                ? _selectionBeforeNew
                : Databases.FirstOrDefault());
        }
        else if (_editSnapshot != null)
        {
            var index = Databases.IndexOf(item);
            if (index >= 0)
            {
                var restored = new DatabaseItemViewModel(CloneDatabase(_editSnapshot));
                DetachDatabase(item);
                AttachDatabase(restored);
                Databases[index] = restored;
                ApplySelection(restored);
            }
        }

        IsDirty = wasDirty;
        StatusText = wasDirty ? "已取消本次编辑，仍有未保存的修改。" : "已取消编辑。";
        ClearEditSession();
    }

    private void ClearEditSession()
    {
        _editSnapshot = null;
        _selectionBeforeNew = null;
        _editingNewDatabase = false;
    }

    private bool HasUnstashedEdits()
    {
        if (_editingNewDatabase)
        {
            return true;
        }

        if (SelectedDatabase == null || _editSnapshot == null)
        {
            return false;
        }

        return !DatabaseEquals(SelectedDatabase.ToModel(), _editSnapshot);
    }

    private static bool DatabaseEquals(DatabaseConnection left, DatabaseConnection right)
    {
        if (!string.Equals(left.Name, right.Name, StringComparison.Ordinal)) return false;
        if (!string.Equals(left.ConnectionString, right.ConnectionString, StringComparison.Ordinal)) return false;
        if (!string.Equals(left.DbType, right.DbType, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals(left.Description ?? string.Empty, right.Description ?? string.Empty, StringComparison.Ordinal)) return false;
        if (left.IsDefault != right.IsDefault) return false;
        if (left.AllowDangerousOperations != right.AllowDangerousOperations) return false;
        return OptimizationEquals(left.OptimizationSettings, right.OptimizationSettings);
    }

    private static bool OptimizationEquals(
        Dictionary<string, string>? left,
        Dictionary<string, string>? right)
    {
        var leftCount = left?.Count ?? 0;
        var rightCount = right?.Count ?? 0;
        if (leftCount != rightCount) return false;
        if (leftCount == 0) return true;

        foreach (var pair in left!)
        {
            if (right is null
                || !right.TryGetValue(pair.Key, out var value)
                || !string.Equals(pair.Value, value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static DatabaseConnection CloneDatabase(DatabaseConnection source) => new()
    {
        Name = source.Name,
        ConnectionString = source.ConnectionString,
        DbType = source.DbType,
        Description = source.Description,
        IsDefault = source.IsDefault,
        AllowDangerousOperations = source.AllowDangerousOperations,
        OptimizationSettings = source.OptimizationSettings == null
            ? null
            : new Dictionary<string, string>(source.OptimizationSettings, StringComparer.Ordinal)
    };

    private void DeleteDatabase()
    {
        if (SelectedDatabase == null) return;
        var index = Databases.IndexOf(SelectedDatabase);
        DetachDatabase(SelectedDatabase);
        Databases.Remove(SelectedDatabase);
        ApplySelection(Databases.ElementAtOrDefault(Math.Max(0, index - 1)));
        MarkDirty();
    }

    private void SetDefault()
    {
        if (SelectedDatabase == null) return;
        SelectedDatabase.IsDefault = true;
        SelectedDatabase.IsDraft = true;
        MarkDirty();
    }

    private void AttachDatabase(DatabaseItemViewModel database)
    {
        void Handler() => OnDatabaseChanged(database);
        _databaseChangedHandlers[database] = Handler;
        database.Changed += Handler;
        database.PropertyChanged += OnDatabasePropertyChanged;
    }

    private void DetachDatabase(DatabaseItemViewModel database)
    {
        if (_databaseChangedHandlers.Remove(database, out var handler))
        {
            database.Changed -= handler;
        }

        database.PropertyChanged -= OnDatabasePropertyChanged;
    }

    private void OnDatabaseChanged(DatabaseItemViewModel database)
    {
        if (_suppressItemDrafts)
        {
            return;
        }

        if (database.IsEditing)
        {
            database.IsDraft = true;
        }

        MarkDirty();
    }

    private void OnDatabasePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DatabaseItemViewModel.IsDefault)
            || sender is not DatabaseItemViewModel selected
            || !selected.IsDefault
            || _normalizingDefaults)
        {
            return;
        }

        try
        {
            _normalizingDefaults = true;
            foreach (var database in Databases)
            {
                if (!ReferenceEquals(database, selected) && database.IsDefault)
                {
                    database.IsDefault = false;
                }
            }
        }
        finally
        {
            _normalizingDefaults = false;
        }
    }

    public void SetDefaultFromTray(DatabaseItemViewModel database)
    {
        if (!Databases.Contains(database))
        {
            return;
        }

        foreach (var item in Databases)
        {
            item.IsDefault = ReferenceEquals(item, database);
        }

        Save();
    }

    public void SetDangerousOperationsFromTray(
        DatabaseItemViewModel database,
        bool enabled)
    {
        if (!Databases.Contains(database))
        {
            return;
        }

        database.AllowDangerousOperations = enabled;
        Save();
    }

    private void Save()
    {
        try
        {
            var source = SelectedConfigSource.Source;
            var path = source == PathSource.EnvironmentVariable
                ? _pathService.NormalizePath(CurrentPath)
                : _pathService.UserProfilePath;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

            var resolution = _pathService.CreateResolution(source, path);
            SetCurrentPath(resolution.Path);
            _store.UseResolution(resolution);

            var config = new DatabasesConfig { Databases = Databases.Select(x => x.ToModel()).ToList() };
            _watcher.SuppressNextChange();
            _store.Save(config);

            if (source == PathSource.EnvironmentVariable)
            {
                _environmentVariableWriter.SetUserEnvironmentVariable(
                    ConfigPathService.EnvironmentVariableName,
                    resolution.Path);
            }
            else
            {
                _environmentVariableWriter.RemoveUserEnvironmentVariable(ConfigPathService.EnvironmentVariableName);
            }

            _watcher.Watch(resolution.Path);
            _sourceConfigurationDirty = false;
            foreach (var database in Databases)
            {
                database.IsDraft = false;
            }

            IsDirty = false;
            IsEditing = false;
            ClearEditSession();
            StatusText = source == PathSource.EnvironmentVariable
                ? $"已保存 {config.Databases.Count} 个连接，并更新环境变量 {ConfigPathService.EnvironmentVariableName}。"
                : $"已保存 {config.Databases.Count} 个连接到用户目录。";

            var defaultDatabaseName = Databases.FirstOrDefault(x => x.IsDefault)?.Name;
            if (!string.IsNullOrWhiteSpace(defaultDatabaseName))
            {
                _ = ActivateDefaultDatabaseAsync(defaultDatabaseName);
            }
        }
        catch (Exception ex)
        {
            StatusText = $"保存失败：{ex.Message}";
        }
    }

    private async Task ActivateDefaultDatabaseAsync(string databaseName)
    {
        var result = await _runtimeActivator.TrySwitchDatabaseAsync(databaseName);
        if (!result.Attempted)
        {
            return;
        }

        StatusText = result.Succeeded
            ? $"已保存，并已激活默认数据库“{databaseName}”。"
            : $"已保存默认数据库“{databaseName}”，但运行时激活失败：{result.Message}。保存的配置将在 MCP Server 重新加载或重启后生效。";
    }
    private async Task TestConnectionAsync()
    {
        if (SelectedDatabase == null) return;
        IsBusy = true;
        StatusText = "正在测试连接…";
        try
        {
            var error = await DatabaseConnectionTester.TestAsync(
                SelectedDatabase.DbType,
                SelectedDatabase.ToModel().ConnectionString);
            StatusText = error == null ? "连接测试成功。" : $"连接测试失败：{error}";
        }
        catch (Exception ex)
        {
            StatusText = $"连接测试失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SetCurrentPath(string path)
    {
        _updatingConfigurationSource = true;
        CurrentPath = path;
        _updatingConfigurationSource = false;
    }

    private void MarkDirty()
    {
        IsDirty = true;
        SaveCommand.RaiseCanExecuteChanged();
        StatusText = "有未保存的修改。";
    }

    private void RaiseCommands()
    {
        EditCommand.RaiseCanExecuteChanged();
        DoneCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        DeleteCommand.RaiseCanExecuteChanged();
        SetDefaultCommand.RaiseCanExecuteChanged();
        TestConnectionCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(HasSelection));
    }

    public void HandleExternalChange()
    {
        if (IsDirty)
        {
            StatusText = "配置文件已在外部修改；当前有未保存修改，请手动刷新。";
            return;
        }

        ReloadCurrentPath();
    }

    public void Dispose()
    {
        foreach (var database in Databases)
        {
            DetachDatabase(database);
        }

        _watcher.Dispose();
    }
}


