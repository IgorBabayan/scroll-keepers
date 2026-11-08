using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows.Data;
using System.Windows.Threading;
using ScrollKeeper.Presentation.Services;
using ScrollKeeper.Presentation.Services.Notifications;
using ScrollKeeper.Presentation.Services.Toasts;
using ScrollKeeper.Presentation.ViewModels.Addons;
using ScrollKeeper.Services.Addons;
using ScrollKeeper.Services.Catalog;
using ScrollKeeper.Services.Images;
using ScrollKeeper.Services.Settings;

namespace ScrollKeeper.Presentation.ViewModels.Shell;

/// <summary>
/// Main window: the Installed and Browse tabs. Keeps one <see cref="AddonItemViewModel"/> per addon and
/// matches the AddOns folder against the ESOUI catalog whenever either changes.
/// </summary>
public sealed partial class MainViewModel : ViewModelBase, IAddonActions
{
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(200);

    // Action center keys: one notification per state.
    private const string AddonUpdatesKey = "addon-updates";
    private const string AddonsFolderKey = "addons-folder";
    private const string CatalogKey = "catalog";

    /// <summary>Addon names listed in the "updates available" notification before "and N more".</summary>
    private const int MaxListedUpdates = 5;

    private readonly ISettingsService _settings;
    private readonly IAddonCatalog _catalog;
    private readonly ILocalAddonScanner _scanner;
    private readonly IInstallRecordStore _records;
    private readonly IAddonInstaller _installer;
    private readonly IDependencyResolver _dependencies;
    private readonly IAddonUninstaller _uninstaller;
    private readonly IImageCache _images;
    private readonly IToastService _toasts;
    private readonly INotificationCenter _notifications;
    private readonly IWindowService _windows;

    private readonly Dictionary<string, AddonItemViewModel> _items = new(StringComparer.Ordinal);
    /// <summary>Addons listed in the last "updates available" notification.</summary>
    private HashSet<string> _notifiedUpdates = new(StringComparer.Ordinal);
    private readonly ObservableCollection<AddonItemViewModel> _installed = [];
    /// <summary>Replaced as a whole when the catalog loads (see <see cref="ApplyCatalog"/>).</summary>
    private List<AddonItemViewModel> _browse = [];
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly DispatcherTimer _installedSearchTimer;
    private readonly DispatcherTimer _browseSearchTimer;

    private AddonCatalogSnapshot? _snapshot;
    private bool _isLoaded;

    /// <summary>Empty-state messages wait for the first scan, so they do not flash at startup.</summary>
    private bool _hasScanned;

    public MainViewModel(
        ISettingsService settings,
        IAddonCatalog catalog,
        ILocalAddonScanner scanner,
        IInstallRecordStore records,
        IAddonInstaller installer,
        IDependencyResolver dependencies,
        IAddonUninstaller uninstaller,
        IImageCache images,
        IToastService toasts,
        INotificationCenter notifications,
        IWindowService windows,
        ActionCenterViewModel actionCenter)
    {
        _settings = settings;
        _catalog = catalog;
        _scanner = scanner;
        _records = records;
        _installer = installer;
        _dependencies = dependencies;
        _uninstaller = uninstaller;
        _images = images;
        _toasts = toasts;
        _notifications = notifications;
        _windows = windows;
        ActionCenter = actionCenter;

        // Installed: grouped into addons and libraries (in that order, groups follow the first sort key);
        // inside a group, addons with updates first, then by name. Live sorting and grouping move a row once it
        // is updated or its manifest changes.
        var installedView = (ListCollectionView)CollectionViewSource.GetDefaultView(_installed);
        installedView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(AddonItemViewModel.Group)));
        installedView.SortDescriptions.Add(new SortDescription(nameof(AddonItemViewModel.GroupOrder), ListSortDirection.Ascending));
        installedView.SortDescriptions.Add(new SortDescription(nameof(AddonItemViewModel.HasUpdate), ListSortDirection.Descending));
        installedView.SortDescriptions.Add(new SortDescription(nameof(AddonItemViewModel.Name), ListSortDirection.Ascending));
        installedView.IsLiveSorting = true;
        installedView.LiveSortingProperties.Add(nameof(AddonItemViewModel.GroupOrder));
        installedView.LiveSortingProperties.Add(nameof(AddonItemViewModel.HasUpdate));
        installedView.LiveSortingProperties.Add(nameof(AddonItemViewModel.Name));
        installedView.IsLiveGrouping = true;
        installedView.LiveGroupingProperties.Add(nameof(AddonItemViewModel.Group));
        installedView.Filter = o => Matches((AddonItemViewModel)o, InstalledSearchText);
        InstalledView = installedView;

        _browseView = CreateBrowseView(_browse);

        SortOptions =
        [
            new BrowseSortOption(BrowseSort.Popular, "Popular"),
            new BrowseSortOption(BrowseSort.RecentlyUpdated, "Recently updated"),
            new BrowseSortOption(BrowseSort.Name, "Name")
        ];
        // Sets the Browse view's CustomSort (OnSelectedSortOptionChanged).
        SelectedSortOption = SortOptions[0];

        _installedSearchTimer = CreateSearchTimer(() => RefreshView(InstalledView));
        _browseSearchTimer = CreateSearchTimer(() => RefreshView(BrowseView));

        _settings.SettingsChanged += OnSettingsChanged;
    }

    /// <summary>The bell in the title bar.</summary>
    public ActionCenterViewModel ActionCenter { get; }

    public ICollectionView InstalledView { get; }

    /// <summary>
    /// A new view is created for every catalog load: filling the old one item by item would raise thousands of
    /// change notifications, and its source must not change while its refresh is deferred.
    /// </summary>
    [ObservableProperty]
    private ListCollectionView _browseView;

    public IReadOnlyList<BrowseSortOption> SortOptions { get; }

    [ObservableProperty]
    private BrowseSortOption _selectedSortOption = null!;

    [ObservableProperty]
    private string _installedSearchText = string.Empty;

    [ObservableProperty]
    private string _browseSearchText = string.Empty;

    /// <summary>0 = Installed, 1 = Browse.</summary>
    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _isLoadingCatalog;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCatalogError))]
    private string? _catalogError;

    public bool HasCatalogError => CatalogError is not null;

    [ObservableProperty]
    private bool _isCatalogFromCache;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAddonsFolderMissing))]
    private string _addonsFolder = string.Empty;

    public bool IsAddonsFolderMissing => _hasScanned && !Directory.Exists(AddonsFolder);

    public int InstalledCount => _installed.Count;

    public int UpdatesCount => _installed.Count(i => i.HasUpdate && i.CanInstall);

    public bool HasUpdates => UpdatesCount > 0;

    public string InstalledHeader => $"Installed ({InstalledCount})";

    public string BrowseHeader => _browse.Count > 0 ? $"Browse ({_browse.Count:N0})" : "Browse";

    public string UpdatesText => UpdatesCount == 1 ? "1 update available" : $"{UpdatesCount} updates available";

    public bool IsInstalledEmpty => _hasScanned && _installed.Count == 0 && !IsAddonsFolderMissing;

    /// <summary>Called once the main window is shown.</summary>
    public async Task InitializeAsync()
    {
        if (_isLoaded)
            return;
        _isLoaded = true;

        // Show what is installed right away, then match it against ESOUI once the catalog arrives.
        await RefreshInstalledAsync();
        await LoadCatalogAsync();
    }

    // ===== Commands =====

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        // Picks up addons added or removed outside ScrollKeeper, then fetches the latest versions.
        await RefreshInstalledAsync();
        await LoadCatalogAsync();
    }

    private bool CanRefresh() => !IsLoadingCatalog;

    [RelayCommand]
    private void OpenSettings() => _windows.ShowSettings();

    /// <summary>Updates every addon that has an update, a few at a time.</summary>
    [RelayCommand]
    private async Task UpdateAllAsync()
    {
        var pending = _installed.Where(i => i.HasUpdate && i.CanInstall && !i.IsBusy).ToList();
        using var throttle = new SemaphoreSlim(3, 3);
        var updated = new List<AddonItemViewModel>();

        await Task.WhenAll(pending.Select(async item =>
        {
            await throttle.WaitAsync();
            try
            {
                // One summary in the action center instead of an entry per addon (failures are still listed).
                if (await InstallCoreAsync(item, notify: false))
                    updated.Add(item);
            }
            finally
            {
                throttle.Release();
            }
        }));

        if (updated.Count == 1)
        {
            var item = updated[0];
            _notifications.Add($"{item.Name} updated", ToastSeverity.Success, $"Version {item.InstalledVersion}")
                .SetAction("Open addon page", () => OpenDetails(item));
        }
        else if (updated.Count > 1)
        {
            var names = updated.Select(i => i.Name).Order(StringComparer.CurrentCultureIgnoreCase).ToList();
            var details = string.Join(", ", names.Take(MaxListedUpdates));
            if (names.Count > MaxListedUpdates)
                details += $" and {names.Count - MaxListedUpdates} more";
            _notifications.Add($"{updated.Count} addons updated", ToastSeverity.Success, details);
        }
    }

    // ===== IAddonActions =====

    public Task InstallAsync(AddonItemViewModel item) => InstallCoreAsync(item, notify: true);

    /// <summary>Installs or updates <paramref name="item"/> and its missing dependencies.</summary>
    /// <param name="notify">Also record a successful install in the action center (toasts are always shown).</param>
    /// <returns><c>true</c> if the addon was installed.</returns>
    private async Task<bool> InstallCoreAsync(AddonItemViewModel item, bool notify)
    {
        if (item.Catalog is not { } addon || item.IsBusy)
            return false;

        var folder = _settings.AddonsFolder;
        if (!EnsureAddonsFolder(folder))
        {
            _toasts.Show("AddOns folder not found", ToastSeverity.Error).Details =
                "Set the path to your Elder Scrolls Online AddOns folder in Settings.";
            return false;
        }

        var wasInstalled = item.IsInstalled;
        // Dependency rows stay busy until the final rescan, so their buttons do not flicker.
        var busy = new List<AddonItemViewModel> { item };
        item.BeginInstall();
        try
        {
            var progress = new Progress<InstallProgress>(item.ReportProgress);
            InstallRecord record;
            try
            {
                record = await _installer.InstallAsync(addon, folder, progress);
            }
            catch (Exception ex) when (IsInstallError(ex))
            {
                Debug.WriteLine($"Install of {addon.Name} failed: {ex}");
                _toasts.Show($"Could not install {item.Name}", ToastSeverity.Error).Details = Describe(ex);
                _notifications.Add($"Could not install {item.Name}", ToastSeverity.Error, Describe(ex))
                    .SetAction("Try again", () => _ = InstallAsync(item));
                return false;
            }

            var dependencies = await InstallDependenciesAsync(item, record, folder, busy);
            await RefreshInstalledAsync();

            var details = $"Version {item.InstalledVersion}";
            if (dependencies.Installed.Count > 0)
                details += $"\nAlso installed: {string.Join(", ", dependencies.Installed)}";
            var title = wasInstalled ? $"{item.Name} updated" : $"{item.Name} installed";
            _toasts.Show(title, ToastSeverity.Success).Details = details;
            if (notify)
                _notifications.Add(title, ToastSeverity.Success, details)
                    .SetAction("Open addon page", () => OpenDetails(item));

            if (dependencies.HasProblems)
            {
                var lines = new List<string>(2);
                if (dependencies.Unavailable.Count > 0)
                    lines.Add($"Not found on ESOUI: {string.Join(", ", dependencies.Unavailable)}");
                if (dependencies.Failed.Count > 0)
                    lines.Add($"Could not install: {string.Join("; ", dependencies.Failed)}");
                var text = string.Join("\n", lines);
                _toasts.Show($"{item.Name} is missing dependencies", ToastSeverity.Warning).Details = text;
                _notifications.Add($"{item.Name} is missing dependencies", ToastSeverity.Warning, text);
            }

            return true;
        }
        catch (Exception ex) when (IsInstallError(ex))
        {
            // The rescan after the install failed; the addon itself is installed.
            Debug.WriteLine($"Refresh after installing {addon.Name} failed: {ex}");
            _toasts.Show("Could not read the AddOns folder", ToastSeverity.Error).Details = Describe(ex);
            return true;
        }
        finally
        {
            foreach (var busyItem in busy)
                busyItem.EndInstall();
        }
    }

    public async Task RemoveAsync(AddonItemViewModel item)
    {
        if (item.Installed is not { } installed || item.IsBusy)
            return;

        var folder = _settings.AddonsFolder;
        RemovalPlan plan;
        try
        {
            plan = await Task.Run(() => _uninstaller.Plan(folder, installed));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _toasts.Show("Could not read the AddOns folder", ToastSeverity.Error).Details = ex.Message;
            return;
        }

        // The addon may have started installing while the folder was read.
        if (item.IsBusy || !_windows.ConfirmRemoval(item.Name, plan))
            return;

        var name = item.Name;
        item.BeginInstall();
        item.ReportStatus("Removing…");
        try
        {
            await Task.Run(() => _uninstaller.Remove(folder, plan));
            item.EndInstall();
            await RefreshInstalledAsync();

            var details = new List<string>(2);
            if (plan.UnusedDependencies.Count > 0)
                details.Add($"Also removed: {string.Join(", ", plan.UnusedDependencies)}");
            if (plan.SavedVariablesFiles.Count > 0)
                details.Add("Saved settings deleted.");
            var text = details.Count > 0 ? string.Join("\n", details) : null;
            _toasts.Show($"{name} removed", ToastSeverity.Success).Details = text;
            _notifications.Add($"{name} removed", ToastSeverity.Success, text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Removal of {name} failed: {ex}");
            var text = $"{ex.Message} {LockedFolderHint(folder)}";
            _toasts.Show($"Could not remove {name}", ToastSeverity.Error).Details = text;
            _notifications.Add($"Could not remove {name}", ToastSeverity.Error, text)
                .SetAction("Try again", () => _ = RemoveAsync(item));
            item.EndInstall();
            // Part of it may be gone already.
            await RefreshInstalledAsync();
        }
        finally
        {
            if (item.IsBusy)
                item.EndInstall();
        }
    }

    public void OpenDetails(AddonItemViewModel item) => _windows.ShowAddonDetails(item);

    // ===== Dependencies =====

    private sealed record DependencyOutcome(
        IReadOnlyList<string> Installed,
        IReadOnlyList<string> Unavailable,
        IReadOnlyList<string> Failed)
    {
        public bool HasProblems => Unavailable.Count > 0 || Failed.Count > 0;
    }

    /// <summary>
    /// Installs (or updates) the required libraries of a freshly installed addon that AddOns does not have
    /// in a sufficient version, then their own requirements, and so on.
    /// Each dependency shows its progress on its own row; <paramref name="owner"/> shows which one is in progress.
    /// </summary>
    private async Task<DependencyOutcome> InstallDependenciesAsync(
        AddonItemViewModel owner,
        InstallRecord record,
        string folder,
        List<AddonItemViewModel> busy)
    {
        var installed = new List<string>();
        var unavailable = new List<string>();
        var failed = new List<string>();

        var catalog = _snapshot?.Addons ?? [];
        var handled = new HashSet<string>(StringComparer.Ordinal) { record.AddonId };
        var pending = new Queue<IReadOnlyList<string>>();
        pending.Enqueue(record.Directories);

        while (pending.TryDequeue(out var directories))
        {
            owner.ReportStatus("Checking dependencies…");
            var missing = await Task.Run(() => _dependencies.FindMissing(folder, directories, catalog));

            foreach (var dependency in missing)
            {
                if (dependency.Source is not { } source)
                {
                    var text = dependency.InstalledAddOnVersion is { } version
                        ? $"{dependency.Dependency} (installed {version})"
                        : dependency.Dependency.ToString();
                    if (!unavailable.Contains(text, StringComparer.OrdinalIgnoreCase))
                        unavailable.Add(text);
                    continue;
                }

                if (!handled.Add(source.Id))
                    continue;

                var item = GetOrCreateItem(source.Id);
                // Already being installed, e.g. by "Update all" or as a dependency of another addon.
                if (item.IsBusy)
                    continue;

                owner.ReportStatus($"Installing {source.Name}…");
                item.BeginInstall();
                busy.Add(item);
                try
                {
                    var progress = new Progress<InstallProgress>(item.ReportProgress);
                    var dependencyRecord = await _installer.InstallAsync(source, folder, progress, asDependency: true);
                    installed.Add(source.Name);
                    pending.Enqueue(dependencyRecord.Directories);
                }
                catch (Exception ex) when (IsInstallError(ex))
                {
                    Debug.WriteLine($"Install of dependency {source.Name} failed: {ex}");
                    failed.Add($"{source.Name} ({Describe(ex)})");
                }
            }
        }

        return new DependencyOutcome(installed, unavailable, failed);
    }

    // ===== Loading =====

    private async Task LoadCatalogAsync()
    {
        if (IsLoadingCatalog)
            return;

        IsLoadingCatalog = true;
        CatalogError = null;
        try
        {
            var snapshot = await _catalog.LoadAsync();
            _snapshot = snapshot;
            IsCatalogFromCache = snapshot.IsFromCache;
            ApplyCatalog(snapshot);
            await RefreshInstalledAsync();

            if (snapshot.IsFromCache)
            {
                const string details = "Showing the addon list from your last visit.";
                _toasts.Show("ESOUI is not reachable", ToastSeverity.Warning).Details = details;
                _notifications.Set(CatalogKey, "ESOUI is not reachable", ToastSeverity.Warning, details, "Try again", Retry);
            }
            else
            {
                _notifications.Remove(CatalogKey);
            }
        }
        catch (Exception ex) when (EsoUiCatalog.IsNetworkError(ex) || ex is InvalidDataException)
        {
            Debug.WriteLine($"Catalog failed: {ex}");
            CatalogError = $"Could not load the addon list from ESOUI. {Describe(ex)}";
            _notifications.Set(CatalogKey, "Could not load the addon list", ToastSeverity.Error, Describe(ex), "Try again", Retry);
        }
        finally
        {
            IsLoadingCatalog = false;
        }
    }

    /// <summary>(Re)creates the Browse list from the catalog, reusing the items that already exist.</summary>
    private void ApplyCatalog(AddonCatalogSnapshot snapshot)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var browse = new List<AddonItemViewModel>(snapshot.Addons.Count);

        foreach (var addon in snapshot.Addons)
        {
            if (!ids.Add(addon.Id))
                continue;

            var item = GetOrCreateItem(addon.Id);
            item.SetCatalog(addon, snapshot.Categories.GetValueOrDefault(addon.CategoryId));
            browse.Add(item);
        }

        // Addons removed from ESOUI keep only their installed state.
        foreach (var item in _items.Values.Where(i => i.Catalog is not null && !ids.Contains(i.Key)))
            item.SetCatalog(null, null);

        _browse = browse;
        BrowseView = CreateBrowseView(browse);

        OnPropertyChanged(nameof(BrowseHeader));
    }

    /// <summary>Scans the AddOns folder and matches it against the catalog (if loaded).</summary>
    private async Task RefreshInstalledAsync()
    {
        await _refreshLock.WaitAsync();
        try
        {
            var folder = _settings.AddonsFolder;
            AddonsFolder = folder;
            var catalog = _snapshot?.Addons ?? [];

            var installed = await Task.Run(() =>
                AddonMatcher.Match(_scanner.Scan(folder), catalog, _records.GetAll(folder)));
            _hasScanned = true;

            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var addon in installed)
            {
                keys.Add(addon.Key);
                var item = GetOrCreateItem(addon.Key);
                item.SetInstalled(addon);
                if (!_installed.Contains(item))
                    _installed.Add(item);
            }

            foreach (var item in _installed.Where(i => !keys.Contains(i.Key)).ToList())
            {
                item.SetInstalled(null);
                _installed.Remove(item);
                // Local-only addons exist only while they are installed.
                if (item.Catalog is null && !item.IsBusy)
                    _items.Remove(item.Key);
            }

            OnPropertyChanged(nameof(InstalledCount));
            OnPropertyChanged(nameof(UpdatesCount));
            OnPropertyChanged(nameof(HasUpdates));
            OnPropertyChanged(nameof(UpdatesText));
            OnPropertyChanged(nameof(InstalledHeader));
            OnPropertyChanged(nameof(IsInstalledEmpty));
            OnPropertyChanged(nameof(IsAddonsFolderMissing));

            UpdateStateNotifications();
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>Keeps the action center in step with the Installed tab: available updates and the AddOns folder.</summary>
    private void UpdateStateNotifications()
    {
        var updates = _installed
            .Where(i => i.HasUpdate && i.CanInstall)
            .OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (updates.Count > 0)
        {
            var details = string.Join(", ", updates.Take(MaxListedUpdates).Select(i => i.Name));
            if (updates.Count > MaxListedUpdates)
                details += $" and {updates.Count - MaxListedUpdates} more";

            // Only new updates are news; fewer left (e.g. while "Update all" runs) just updates the text.
            var keys = updates.Select(i => i.Key).ToHashSet(StringComparer.Ordinal);
            var isNews = !keys.IsSubsetOf(_notifiedUpdates);
            _notifiedUpdates = keys;

            _notifications.Set(AddonUpdatesKey, UpdatesText, ToastSeverity.Info, details, "Update all",
                () => UpdateAllCommand.Execute(null), isNews);
        }
        else
        {
            _notifiedUpdates.Clear();
            _notifications.Remove(AddonUpdatesKey);
        }

        if (IsAddonsFolderMissing)
            _notifications.Set(AddonsFolderKey, "AddOns folder not found", ToastSeverity.Error,
                $"Nothing was found at {AddonsFolder}.", "Open Settings", OpenSettings);
        else
            _notifications.Remove(AddonsFolderKey);
    }

    private void Retry()
    {
        if (RefreshCommand.CanExecute(null))
            RefreshCommand.Execute(null);
    }

    private AddonItemViewModel GetOrCreateItem(string key)
    {
        if (!_items.TryGetValue(key, out var item))
            _items[key] = item = new AddonItemViewModel(key, this, _images);
        return item;
    }

    private async void OnSettingsChanged(object? sender, AppSettings settings)
    {
        try
        {
            if (!string.Equals(_settings.AddonsFolder, AddonsFolder, StringComparison.OrdinalIgnoreCase))
                await RefreshInstalledAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _toasts.Show("Could not read the AddOns folder", ToastSeverity.Error).Details = ex.Message;
        }
    }

    // ===== Search & sort =====

    partial void OnInstalledSearchTextChanged(string value) => Restart(_installedSearchTimer);

    partial void OnBrowseSearchTextChanged(string value) => Restart(_browseSearchTimer);

    partial void OnSelectedSortOptionChanged(BrowseSortOption value)
    {
        if (value is not null)
            BrowseView.CustomSort = value.Comparer;
    }

    /// <summary>Filtered by the Browse search box and sorted by the selected option.</summary>
    private ListCollectionView CreateBrowseView(List<AddonItemViewModel> items)
    {
        var view = new ListCollectionView(items)
        {
            Filter = o => Matches((AddonItemViewModel)o, BrowseSearchText)
        };

        // SelectedSortOption is still null while the constructor creates the first (empty) view.
        if (SelectedSortOption is { } sort)
            view.CustomSort = sort.Comparer;
        return view;
    }

    private static bool Matches(AddonItemViewModel item, string search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return true;

        var text = search.Trim();
        return item.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)
               || item.Author.Contains(text, StringComparison.CurrentCultureIgnoreCase)
               || (item.CategoryTitle?.Contains(text, StringComparison.CurrentCultureIgnoreCase) ?? false)
               || (item.Installed?.Folders.Any(f => f.FolderName.Contains(text, StringComparison.OrdinalIgnoreCase)) ?? false);
    }

    private static DispatcherTimer CreateSearchTimer(Action refresh)
    {
        var timer = new DispatcherTimer { Interval = SearchDelay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            refresh();
        };
        return timer;
    }

    private static void Restart(DispatcherTimer timer)
    {
        timer.Stop();
        timer.Start();
    }

    private static void RefreshView(ICollectionView view) => view.Refresh();

    // ===== Helpers =====

    /// <summary>
    /// The game creates AddOns the first time it runs with addons enabled; create it when its server folder
    /// (<c>live</c> / <c>liveeu</c>) exists.
    /// </summary>
    private static bool EnsureAddonsFolder(string folder)
    {
        if (Directory.Exists(folder))
            return true;

        var parent = Path.GetDirectoryName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (parent is null || !Directory.Exists(parent))
            return false;

        try
        {
            Directory.CreateDirectory(folder);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsInstallError(Exception ex) =>
        ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException
            or OperationCanceledException or ArgumentException or System.Text.Json.JsonException;

    /// <summary>What usually holds an addon folder open, besides the game.</summary>
    private static string LockedFolderHint(string addonsFolder) =>
        addonsFolder.Contains("OneDrive", StringComparison.OrdinalIgnoreCase)
            ? "Close the game and any Explorer window showing that folder. Your AddOns folder is in OneDrive: pause OneDrive syncing and try again."
            : "Close the game and any Explorer window showing that folder, then try again.";

    private static string Describe(Exception ex) => ex switch
    {
        OperationCanceledException => "The request timed out.",
        HttpRequestException { StatusCode: { } code } => $"ESOUI answered {(int)code} ({code}).",
        _ => ex.Message
    };
}
