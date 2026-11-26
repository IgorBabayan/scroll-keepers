using Avalonia;
using Avalonia.Styling;

namespace ScrollKeeper.Services.Theming;

/// <summary>
/// The palettes are theme dictionaries of App.axaml, so Avalonia swaps them by itself: this service makes the app
/// follow the system theme variant and reports which palette is active.
/// </summary>
public sealed class ThemeService : IThemeService, IDisposable
{
    private Application? _application;

    public AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;

    public void Initialize()
    {
        if (_application is not null || Application.Current is not { } application)
            return;

        _application = application;

        // Default = follow the operating system, live.
        application.RequestedThemeVariant = ThemeVariant.Default;
        application.ActualThemeVariantChanged += OnActualThemeVariantChanged;
        Update(application);
    }

    public void Dispose()
    {
        if (_application is not null)
            _application.ActualThemeVariantChanged -= OnActualThemeVariantChanged;
        _application = null;
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        if (_application is not null)
            Update(_application);
    }

    private void Update(Application application) =>
        CurrentTheme = application.ActualThemeVariant == ThemeVariant.Light ? AppTheme.Light : AppTheme.Dark;
}
