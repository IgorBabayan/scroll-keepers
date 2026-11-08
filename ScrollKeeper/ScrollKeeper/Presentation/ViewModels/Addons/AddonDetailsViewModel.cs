using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using ScrollKeeper.Presentation.Documents;
using ScrollKeeper.Presentation.Services;
using ScrollKeeper.Services.Catalog;
using ScrollKeeper.Services.Images;

namespace ScrollKeeper.Presentation.ViewModels.Addons;

/// <summary>
/// The addon page dialog: everything ESOUI shows about an addon (description, change log, pictures,
/// statistics, compatibility) plus Install / Update with the same progress as the lists.
/// </summary>
public sealed partial class AddonDetailsViewModel : ViewModelBase
{
    /// <summary>Tiles are 180 x 120 px and filled; decode the shorter side at twice that for high-DPI screens.</summary>
    private const int ScreenshotDecodeSize = 360;

    private readonly IAddonCatalog _catalog;
    private readonly IImageCache _images;
    private readonly IWindowService _windows;

    public AddonDetailsViewModel(AddonItemViewModel item, IAddonCatalog catalog, IImageCache images, IWindowService windows)
    {
        Item = item;
        _catalog = catalog;
        _images = images;
        _windows = windows;

        var addon = item.Catalog;
        if (addon is not null)
        {
            for (var i = 0; i < Math.Max(addon.Thumbnails.Count, addon.Images.Count); i++)
            {
                var thumbnail = i < addon.Thumbnails.Count ? addon.Thumbnails[i] : addon.Images[i];
                var image = i < addon.Images.Count ? addon.Images[i] : thumbnail;
                Screenshots.Add(new ScreenshotViewModel(thumbnail, image));
            }
        }

        // Local-only addons: what their manifest says.
        if (addon is null && item.Installed is { } installed)
            Description = installed.MainFolder.Manifest.Description;

        // Installed version, folders etc. change when the addon is installed or updated from this page.
        Item.PropertyChanged += OnItemPropertyChanged;
    }

    public AddonItemViewModel Item { get; }

    public string Title => Item.Name;

    public bool IsOnEsoui => Item.Catalog is not null;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    public bool HasError => Error is not null;

    /// <summary>Description in ESOUI BBCode (rendered by <see cref="BbCode"/>).</summary>
    [ObservableProperty]
    private string? _description;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChangeLog))]
    private string? _changeLog;

    public bool HasChangeLog => !string.IsNullOrWhiteSpace(ChangeLog);

    [ObservableProperty]
    private bool _isPending;

    [ObservableProperty]
    private string? _viewsText;

    public ObservableCollection<ScreenshotViewModel> Screenshots { get; } = [];

    public bool HasScreenshots => Screenshots.Count > 0;

    /// <summary>Label / value pairs of the information panel.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Facts
    {
        get
        {
            var facts = new List<KeyValuePair<string, string>>();
            void Add(string label, string? value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    facts.Add(new KeyValuePair<string, string>(label, value));
            }

            var addon = Item.Catalog;
            Add("Author", Item.Author);
            Add("Category", Item.CategoryTitle);
            Add("Version", addon?.Version);
            Add("Installed", Item.IsInstalled ? (string.IsNullOrWhiteSpace(Item.InstalledVersion) ? "Yes" : Item.InstalledVersion) : null);
            Add("Updated", addon?.Updated?.LocalDateTime.ToString("g", CultureInfo.CurrentCulture));
            if (addon is not null)
            {
                Add("Downloads", addon.Downloads.ToString("N0", CultureInfo.CurrentCulture));
                Add("Monthly downloads", addon.MonthlyDownloads.ToString("N0", CultureInfo.CurrentCulture));
                Add("Favorites", addon.Favorites.ToString("N0", CultureInfo.CurrentCulture));
            }

            Add("Views", ViewsText);
            Add("Compatible with", addon is null
                ? null
                : string.Join(", ", addon.Compatibility.Select(c => string.IsNullOrWhiteSpace(c.Name) ? c.Version : $"{c.Name} ({c.Version})")));

            var folders = Item.Installed?.Folders.Select(f => f.FolderName) ?? addon?.Directories ?? [];
            Add("Folders", string.Join(", ", folders));
            if (Item.IsLibrary)
                Add("Type", "Library");
            return facts;
        }
    }

    /// <summary>Called when the dialog opens: downloads the description, change log and screenshots.</summary>
    public async Task LoadAsync()
    {
        foreach (var screenshot in Screenshots)
            _ = screenshot.LoadAsync(_images, ScreenshotDecodeSize);

        if (Item.Catalog is not { } addon)
            return;

        IsLoading = true;
        Error = null;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var details = await _catalog.GetDetailsAsync(addon.Id, timeout.Token);
            Description = details.Description;
            ChangeLog = details.ChangeLog;
            IsPending = details.IsPending;
            ViewsText = details.Views > 0
                ? $"{details.Views.ToString("N0", CultureInfo.CurrentCulture)} ({details.MonthlyViews.ToString("N0", CultureInfo.CurrentCulture)} this month)"
                : null;
            OnPropertyChanged(nameof(Facts));
        }
        catch (Exception ex) when (EsoUiCatalog.IsNetworkError(ex) || ex is InvalidDataException or ArgumentException)
        {
            Debug.WriteLine($"Details of {addon.Name} failed: {ex}");
            Error = "Could not load the addon page from ESOUI.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Called by the view when the dialog closes; the item outlives it.</summary>
    public void OnClosed() => Item.PropertyChanged -= OnItemPropertyChanged;

    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AddonItemViewModel.InstalledVersion) or nameof(AddonItemViewModel.IsInstalled)
            or nameof(AddonItemViewModel.CategoryTitle) or nameof(AddonItemViewModel.LatestVersion))
            OnPropertyChanged(nameof(Facts));
    }

    [RelayCommand]
    private void OpenWebPage()
    {
        if (Uri.TryCreate(Item.Catalog?.InfoUrl, UriKind.Absolute, out var uri))
            BbCodeDocumentBuilder.OpenInBrowser(uri);
    }

    [RelayCommand]
    private void OpenScreenshot(ScreenshotViewModel? screenshot)
    {
        if (screenshot is null)
            return;

        var index = Screenshots.IndexOf(screenshot);
        if (index < 0)
            return;

        _windows.ShowImageViewer(Title, Screenshots.ToList(), index);
    }
}

/// <summary>A picture of the addon page; the full-size image opens in the picture viewer.</summary>
public sealed partial class ScreenshotViewModel(string thumbnailUrl, string imageUrl) : ObservableObject
{
    public string ThumbnailUrl { get; } = thumbnailUrl;

    public string ImageUrl { get; } = imageUrl;

    [ObservableProperty]
    private ImageSource? _image;

    internal async Task LoadAsync(IImageCache images, int decodeSize)
    {
        if (Image is not null)
            return;

        // ESOUI thumbnails are tiny and blurry when stretched over a tile: show one while the full picture
        // downloads, then swap in the full picture (decoded small, and already on disk for the viewer).
        var thumbnail = await images.GetAsync(ThumbnailUrl, decodeSize);
        if (thumbnail is not null)
            Image = thumbnail;

        if (ImageUrl == ThumbnailUrl)
            return;

        var full = await images.GetAsync(ImageUrl, decodeSize);
        if (full is not null)
            Image = full;
    }
}
