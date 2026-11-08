using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows.Data;
using System.Windows.Threading;
using ScrollKeeper.Presentation.Services;
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

    private readonly ISettingsService _settings;
    private readonly IAddonCatalog _catalog;
    private readonly ILocalAddonScanner _scanner;
    private readonly IInstallRecordStore _records;
    private readonly IAddonInstaller _installer;
    private readonly IImageCache _images;
    private readonly IToastService _toasts;
    private readonly IWindowService _windows;

    private readonly Dictionary<string, AddonItemViewModel> _items = new(StringComparer.Ordinal);
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
        IImageCache images,
        IToastService toasts,
        IWindowService windows)
    {
        _settings = settings;
        _catalog = catalog;
        _scanner = scanner;
        _records = records;
        _installer = installer;
        _images = images;
        _toasts = toasts;
        _windows = windows;

        // Installed: addons with updates first, then by name. Live sorting moves a row once it is updated.
        var installedView = (ListCollectionView)CollectionViewSource.GetDefaultView(_installed);
        installedView.SortDescriptions.Add(new SortDescription(nameof(AddonItemViewModel.HasUpdate), ListSortDirection.Descending));
        installedView.SortDescriptions.Add(new SortDescription(nameof(AddonItemViewModel.Name), ListSortDirection.Ascending));
        installedView.IsLiveSorting = true;
        installedView.LiveSortingProperties.Add(nameof(AddonItemViewModel.HasUpdate));
        installedView.LiveSortingProperties.Add(nameof(AddonItemViewModel.Name));
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

        await Task.WhenAll(pending.Select(async item =>
        {
            await throttle.WaitAsync();
            try
            {
                await InstallAsync(item);
            }
            finally
            {
                throttle.Release();
            }
        }));
    }

    // ===== IAddonActions =====

    public async Task InstallAsync(AddonItemViewModel item)
    {
        if (item.Catalog is not { } addon || item.IsBusy)
            return;

        var folder = _settings.AddonsFolder;
        if (!EnsureAddonsFolder(folder))
        {
            _toasts.Show("AddOns folder not found", ToastSeverity.Error).Details =
                "Set the path to your Elder Scrolls Online AddOns folder in Settings.";
            return;
        }

        var wasInstalled = item.IsInstalled;
        item.BeginInstall();
        try
        {
            var progress = new Progress<InstallProgress>(item.ReportProgress);
            await _installer.InstallAsync(addon, folder, progress);
            await RefreshInstalledAsync();

            _toasts.Show(wasInstalled ? $"{item.Name} updated" : $"{item.Name} installed", ToastSeverity.Success)
                .Details = $"Version {item.InstalledVersion}";
        }
        catch (Exception ex) when (IsInstallError(ex))
        {
            Debug.WriteLine($"Install of {addon.Name} failed: {ex}");
            _toasts.Show($"Could not install {item.Name}", ToastSeverity.Error).Details = Describe(ex);
        }
        finally
        {
            item.EndInstall();
        }
    }

    public void OpenDetails(AddonItemViewModel item) => _windows.ShowAddonDetails(item);

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
                _toasts.Show("ESOUI is not reachable", ToastSeverity.Warning).Details = "Showing the addon list from your last visit.";
        }
        catch (Exception ex) when (EsoUiCatalog.IsNetworkError(ex) || ex is InvalidDataException)
        {
            Debug.WriteLine($"Catalog failed: {ex}");
            CatalogError = $"Could not load the addon list from ESOUI. {Describe(ex)}";
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
        }
        finally
        {
            _refreshLock.Release();
        }
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

    private static string Describe(Exception ex) => ex switch
    {
        OperationCanceledException => "The request timed out.",
        HttpRequestException { StatusCode: { } code } => $"ESOUI answered {(int)code} ({code}).",
        _ => ex.Message
    };
}
