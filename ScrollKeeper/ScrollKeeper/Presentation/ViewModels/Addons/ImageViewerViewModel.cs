using System.Windows.Media;
using ScrollKeeper.Presentation.Documents;
using ScrollKeeper.Services.Images;

namespace ScrollKeeper.Presentation.ViewModels.Addons;

/// <summary>
/// The picture viewer dialog: shows the full-size pictures of an addon page one at a time,
/// with previous / next navigation. The thumbnail is shown while the full picture downloads.
/// </summary>
public sealed partial class ImageViewerViewModel : ViewModelBase
{
    private readonly IReadOnlyList<ScreenshotViewModel> _pictures;
    private readonly IImageCache _images;
    private int _loadVersion;

    public ImageViewerViewModel(string title, IReadOnlyList<ScreenshotViewModel> pictures, int index, IImageCache images)
    {
        ArgumentNullException.ThrowIfNull(pictures);
        if (pictures.Count == 0)
            throw new ArgumentException("At least one picture is required.", nameof(pictures));

        Title = title;
        _pictures = pictures;
        _images = images;
        _index = Math.Clamp(index, 0, pictures.Count - 1);
    }

    public string Title { get; }

    public bool HasMany => _pictures.Count > 1;

    public string Position => $"{Index + 1} / {_pictures.Count}";

    private ScreenshotViewModel Current => _pictures[Index];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Position))]
    private int _index;

    [ObservableProperty]
    private ImageSource? _image;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasError;

    /// <summary>Called when the dialog opens.</summary>
    public Task LoadAsync() => LoadCurrentAsync();

    [RelayCommand(CanExecute = nameof(HasMany))]
    private void Previous()
    {
        Index = (Index - 1 + _pictures.Count) % _pictures.Count;
        _ = LoadCurrentAsync();
    }

    [RelayCommand(CanExecute = nameof(HasMany))]
    private void Next()
    {
        Index = (Index + 1) % _pictures.Count;
        _ = LoadCurrentAsync();
    }

    [RelayCommand]
    private void OpenInBrowser()
    {
        if (Uri.TryCreate(Current.ImageUrl, UriKind.Absolute, out var uri))
            BbCodeDocumentBuilder.OpenInBrowser(uri);
    }

    private async Task LoadCurrentAsync()
    {
        var version = ++_loadVersion;
        var picture = Current;

        // The (already downloaded) thumbnail fills the gap until the full picture arrives.
        Image = picture.Image;
        HasError = false;
        IsLoading = true;

        var full = await _images.GetAsync(picture.ImageUrl, 0);

        // The user may have moved to another picture while this one was downloading.
        if (version != _loadVersion)
            return;

        IsLoading = false;
        if (full is not null)
            Image = full;
        else
            HasError = true;
    }
}
