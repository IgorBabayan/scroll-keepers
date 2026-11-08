using System.Globalization;
using System.Windows.Media;
using ScrollKeeper.Services.Addons;
using ScrollKeeper.Services.Catalog;
using ScrollKeeper.Services.Images;

namespace ScrollKeeper.Presentation.ViewModels.Addons;

/// <summary>
/// One addon, as shown in the Installed and Browse lists and on the addon page. A single instance exists per addon,
/// so an install started anywhere shows its progress everywhere.
/// </summary>
public sealed partial class AddonItemViewModel : ViewModelBase
{
    /// <summary>Icons are drawn at 44 px; decode at twice that for high-DPI screens.</summary>
    private const int IconDecodeWidth = 96;

    private readonly IAddonActions _actions;
    private readonly IImageCache _images;

    private ImageSource? _icon;
    private string? _requestedIconUrl;

    public AddonItemViewModel(string key, IAddonActions actions, IImageCache images)
    {
        Key = key;
        _actions = actions;
        _images = images;
    }

    /// <summary>ESOUI id, or <c>local:Folder</c> for addons ESOUI does not know.</summary>
    public string Key { get; }

    /// <summary>The ESOUI entry, or <c>null</c> for a local-only addon.</summary>
    public CatalogAddon? Catalog { get; private set; }

    /// <summary>What is installed, or <c>null</c> when the addon is not installed.</summary>
    public InstalledAddon? Installed { get; private set; }

    public string? CategoryTitle { get; private set; }

    private string? CategoryIconUrl { get; set; }

    // ===== Display =====

    public string Name => Catalog?.Name ?? Installed?.MainFolder.Manifest.Title ?? Key;

    public string Initial => Name.FirstOrDefault(char.IsLetterOrDigit) is var c && c != default
        ? char.ToUpperInvariant(c).ToString()
        : "?";

    public string Author
    {
        get
        {
            var author = Catalog?.Author;
            if (string.IsNullOrWhiteSpace(author))
                author = Installed?.MainFolder.Manifest.Author;
            return author ?? string.Empty;
        }
    }

    /// <summary>"by Author · Category"</summary>
    public string Byline
    {
        get
        {
            var parts = new List<string>(2);
            if (!string.IsNullOrWhiteSpace(Author))
                parts.Add($"by {Author}");
            if (!string.IsNullOrWhiteSpace(CategoryTitle))
                parts.Add(CategoryTitle);
            else if (Catalog is null)
                parts.Add("Not on ESOUI");
            return string.Join(" · ", parts);
        }
    }

    public string LatestVersion => Catalog?.Version ?? string.Empty;

    public string InstalledVersion => Installed?.InstalledVersion ?? string.Empty;

    public long Downloads => Catalog?.Downloads ?? 0;

    public DateTimeOffset? Updated => Catalog?.Updated;

    public bool IsLibrary => Installed?.MainFolder.Manifest.IsLibrary == true;

    /// <summary>"v2.3 · Updated 03.11.2026 · 1.2M downloads"</summary>
    public string Details
    {
        get
        {
            var parts = new List<string>(3);
            if (IsInstalled && HasUpdate)
                parts.Add($"{FormatVersion(InstalledVersion)} → {FormatVersion(LatestVersion)}");
            else if (IsInstalled && !string.IsNullOrWhiteSpace(InstalledVersion))
                parts.Add(FormatVersion(InstalledVersion));
            else if (!string.IsNullOrWhiteSpace(LatestVersion))
                parts.Add(FormatVersion(LatestVersion));

            if (Updated is { } updated)
                parts.Add($"Updated {updated.LocalDateTime.ToString("d", CultureInfo.CurrentCulture)}");
            if (Catalog is not null)
                parts.Add($"{FormatCount(Downloads)} downloads");
            return string.Join(" · ", parts);
        }
    }

    /// <summary>Own picture: the first screenshot thumbnail, otherwise the ESOUI category icon.</summary>
    public string? IconUrl => Catalog?.Thumbnails.FirstOrDefault() ?? CategoryIconUrl;

    /// <summary>Loaded on first use, so only rows scrolled into view download their icon.</summary>
    public ImageSource? Icon
    {
        get
        {
            var url = IconUrl;
            if (url is not null && url != _requestedIconUrl)
            {
                _requestedIconUrl = url;
                _ = LoadIconAsync(url);
            }

            return _icon;
        }
    }

    public bool HasIcon => _icon is not null;

    // ===== State =====

    public bool IsInstalled => Installed is not null;

    public bool HasUpdate => Installed?.HasUpdate == true;

    public bool CanInstall => Catalog is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInstallButton), nameof(ShowUpdateButton), nameof(ShowInstalledLabel))]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private bool _isBusy;

    /// <summary>0..100 of the current stage (download, then installation).</summary>
    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isProgressIndeterminate;

    [ObservableProperty]
    private string _progressText = string.Empty;

    /// <summary>Browse: not installed yet.</summary>
    public bool ShowInstallButton => !IsInstalled && CanInstall && !IsBusy;

    /// <summary>Installed (and Browse for installed addons): ESOUI has a newer version.</summary>
    public bool ShowUpdateButton => IsInstalled && HasUpdate && CanInstall && !IsBusy;

    /// <summary>Installed and up to date.</summary>
    public bool ShowInstalledLabel => IsInstalled && !HasUpdate && !IsBusy;

    /// <summary>Install and Update are the same operation: the archive replaces the addon's folders.</summary>
    [RelayCommand(CanExecute = nameof(CanRunInstall))]
    private Task InstallAsync() => _actions.InstallAsync(this);

    private bool CanRunInstall() => CanInstall && !IsBusy;

    [RelayCommand]
    private void OpenDetails() => _actions.OpenDetails(this);

    // ===== Updates from the shell =====

    public void SetCatalog(CatalogAddon? catalog, CatalogCategory? category)
    {
        Catalog = catalog;
        CategoryTitle = category?.Title;
        CategoryIconUrl = category?.IconUrl;
        RaiseAll();
    }

    public void SetInstalled(InstalledAddon? installed)
    {
        if (ReferenceEquals(Installed, installed))
            return;

        Installed = installed;
        RaiseAll();
    }

    public void BeginInstall()
    {
        Progress = 0;
        IsProgressIndeterminate = true;
        ProgressText = "Preparing…";
        IsBusy = true;
    }

    public void ReportProgress(InstallProgress progress)
    {
        IsProgressIndeterminate = progress.Fraction is null;
        Progress = (progress.Fraction ?? 0) * 100;

        var verb = progress.Stage == InstallStage.Downloading ? "Downloading" : "Installing";
        ProgressText = progress.Fraction is { } fraction
            ? $"{verb}… {fraction:P0}"
            : $"{verb}…";
    }

    /// <summary>Shows a step without a known fraction, e.g. while dependencies are installed.</summary>
    public void ReportStatus(string text)
    {
        IsProgressIndeterminate = true;
        Progress = 0;
        ProgressText = text;
    }

    public void EndInstall()
    {
        IsBusy = false;
        IsProgressIndeterminate = false;
        Progress = 0;
        ProgressText = string.Empty;
    }

    private async Task LoadIconAsync(string url)
    {
        var image = await _images.GetAsync(url, IconDecodeWidth);
        // The addon may have changed (catalog refresh) while the image was downloading.
        if (image is null || url != IconUrl)
            return;

        _icon = image;
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(HasIcon));
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Initial));
        OnPropertyChanged(nameof(Author));
        OnPropertyChanged(nameof(Byline));
        OnPropertyChanged(nameof(LatestVersion));
        OnPropertyChanged(nameof(InstalledVersion));
        OnPropertyChanged(nameof(Downloads));
        OnPropertyChanged(nameof(Updated));
        OnPropertyChanged(nameof(IsLibrary));
        OnPropertyChanged(nameof(Details));
        OnPropertyChanged(nameof(IconUrl));
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(IsInstalled));
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(ShowInstallButton));
        OnPropertyChanged(nameof(ShowUpdateButton));
        OnPropertyChanged(nameof(ShowInstalledLabel));
        InstallCommand.NotifyCanExecuteChanged();
    }

    private static string FormatVersion(string version) =>
        version.Length > 0 && char.IsAsciiDigit(version[0]) ? "v" + version : version;

    internal static string FormatCount(long count) => count switch
    {
        >= 1_000_000 => (count / 1_000_000d).ToString("0.#", CultureInfo.CurrentCulture) + "M",
        >= 10_000 => (count / 1_000d).ToString("0", CultureInfo.CurrentCulture) + "K",
        >= 1_000 => (count / 1_000d).ToString("0.#", CultureInfo.CurrentCulture) + "K",
        _ => count.ToString("N0", CultureInfo.CurrentCulture)
    };
}
