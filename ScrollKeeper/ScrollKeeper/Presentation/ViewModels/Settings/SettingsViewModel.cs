using System.ComponentModel;
using System.Globalization;
using System.IO;
using ScrollKeeper.Presentation.Services;
using ScrollKeeper.Services.Settings;
using ScrollKeeper.Services.Updates;

namespace ScrollKeeper.Presentation.ViewModels.Settings;

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly IFolderPickerService _folderPicker;
    private readonly IAutoUpdater _autoUpdater;

    public SettingsViewModel(
        ISettingsService settingsService,
        IFolderPickerService folderPicker,
        IUpdateService updateService,
        IAutoUpdater autoUpdater)
    {
        _settingsService = settingsService;
        _folderPicker = folderPicker;
        _autoUpdater = autoUpdater;

        var settings = settingsService.Current;

        // Documents\Elder Scrolls Online\live\AddOns unless another folder was chosen.
        AddonsFolderPath = settingsService.AddonsFolder;

        AutoUpdate = settings.AutoUpdate;
        CurrentVersionText = $"Current version: {updateService.CurrentVersion.ToString(3)}";
        IsNotInstalled = !updateService.IsInstalled;
        RestartDelayText = settings.RestartDelaySeconds.ToString(CultureInfo.InvariantCulture);

        // The updater is a singleton that outlives this dialog: unsubscribed in OnClosed.
        _autoUpdater.PropertyChanged += OnAutoUpdaterPropertyChanged;
    }

    /// <summary>Asks the view to close: <c>true</c> = saved, <c>false</c> = cancelled.</summary>
    public event EventHandler<bool>? CloseRequested;

    /// <summary>The game's AddOns folder.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAddonsFolderMissing))]
    [NotifyPropertyChangedFor(nameof(LooksLikeWrongFolder))]
    private string _addonsFolderPath = string.Empty;

    /// <summary>Check GitHub for a new release on every start and install it automatically.</summary>
    [ObservableProperty]
    private bool _autoUpdate;

    public string CurrentVersionText { get; }

    /// <summary>Text of the "Seconds before auto-restart" box; digits only (see NumericInput).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRestartDelayInvalid))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(IncreaseRestartDelayCommand))]
    [NotifyCanExecuteChangedFor(nameof(DecreaseRestartDelayCommand))]
    private string _restartDelayText = string.Empty;

    public int MaxRestartDelaySeconds => AppSettings.MaxRestartDelaySeconds;

    public bool IsRestartDelayInvalid => !TryGetRestartDelay(out _);

    /// <summary>Result of the last check, shown under the update buttons.</summary>
    public string? AvailableUpdateText => _autoUpdater.AvailableUpdate is { } update
        ? $"Version {update.Version.ToString(3)} is available."
        : null;

    public bool HasAvailableUpdate => _autoUpdater.AvailableUpdate is not null;

    /// <summary>True for development builds / unpacked copies, which are never updated.</summary>
    public bool IsNotInstalled { get; }

    /// <summary>True when a path is entered but the folder does not exist.</summary>
    public bool IsAddonsFolderMissing =>
        !string.IsNullOrWhiteSpace(AddonsFolderPath) && !Directory.Exists(AddonsFolderPath.Trim());

    /// <summary>The folder exists but is not named AddOns (e.g. the "live" folder was picked).</summary>
    public bool LooksLikeWrongFolder =>
        !IsAddonsFolderMissing
        && !string.IsNullOrWhiteSpace(AddonsFolderPath)
        && !string.Equals(Path.GetFileName(AddonsFolderPath.Trim().TrimEnd('\\', '/')), "AddOns", StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    private void SelectFolder()
    {
        var current = AddonsFolderPath.Trim();
        var initial = Directory.Exists(current) ? current : EsoPaths.GameDocumentsFolder;

        var folder = _folderPicker.PickFolder("Select the Elder Scrolls Online AddOns folder", initial);
        if (folder is not null)
            AddonsFolderPath = folder;
    }

    [RelayCommand]
    private void ResetFolder() => AddonsFolderPath = EsoPaths.FindAddonsFolder();

    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    private Task CheckForUpdatesAsync() => _autoUpdater.CheckForUpdatesAsync();

    private bool CanCheckForUpdates() => !_autoUpdater.IsBusy;

    /// <summary>Enabled once "Check for updates" (or the startup check) found a newer version.</summary>
    [RelayCommand(CanExecute = nameof(CanDownloadAndUpdate))]
    private Task DownloadAndUpdateAsync() =>
        // Use the value in the box even if Settings was not saved yet.
        _autoUpdater.DownloadAndInstallAsync(TryGetRestartDelay(out var seconds) ? seconds : null);

    private bool CanDownloadAndUpdate() =>
        _autoUpdater.AvailableUpdate is not null && _autoUpdater.CanInstall && !_autoUpdater.IsBusy;

    [RelayCommand(CanExecute = nameof(CanIncreaseRestartDelay))]
    private void IncreaseRestartDelay() =>
        RestartDelayText = (CurrentRestartDelay() + 1).ToString(CultureInfo.InvariantCulture);

    private bool CanIncreaseRestartDelay() => CurrentRestartDelay() < AppSettings.MaxRestartDelaySeconds;

    [RelayCommand(CanExecute = nameof(CanDecreaseRestartDelay))]
    private void DecreaseRestartDelay() =>
        RestartDelayText = (CurrentRestartDelay() - 1).ToString(CultureInfo.InvariantCulture);

    private bool CanDecreaseRestartDelay() => CurrentRestartDelay() > 0;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        var path = AddonsFolderPath.Trim();
        TryGetRestartDelay(out var restartDelay);

        // The detected default is not stored, so it keeps following the game's folder.
        var isDefault = string.Equals(path, EsoPaths.FindAddonsFolder(), StringComparison.OrdinalIgnoreCase);

        _settingsService.Save(_settingsService.Current with
        {
            AddonsFolderPath = string.IsNullOrEmpty(path) || isDefault ? null : path,
            AutoUpdate = AutoUpdate,
            RestartDelaySeconds = restartDelay
        });

        CloseRequested?.Invoke(this, true);
    }

    private bool CanSave() => !IsRestartDelayInvalid;

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);

    /// <summary>Called by the view when the window closes by any means (Cancel, Esc, Alt+F4, ×).</summary>
    public void OnClosed() => _autoUpdater.PropertyChanged -= OnAutoUpdaterPropertyChanged;

    private void OnAutoUpdaterPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(AvailableUpdateText));
        OnPropertyChanged(nameof(HasAvailableUpdate));
        CheckForUpdatesCommand.NotifyCanExecuteChanged();
        DownloadAndUpdateCommand.NotifyCanExecuteChanged();
    }

    private bool TryGetRestartDelay(out int seconds) =>
        int.TryParse(RestartDelayText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out seconds)
        && seconds is >= 0 and <= AppSettings.MaxRestartDelaySeconds;

    /// <summary>Current value for the +/- buttons; an empty or invalid box counts as the default.</summary>
    private int CurrentRestartDelay() =>
        int.TryParse(RestartDelayText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            ? Math.Clamp(seconds, 0, AppSettings.MaxRestartDelaySeconds)
            : new AppSettings().RestartDelaySeconds;
}
