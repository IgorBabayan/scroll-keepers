using System.Windows;
using Microsoft.Win32;

namespace ScrollKeeper.Services.Theming;

public sealed class ThemeService : IThemeService, IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private bool _isFollowingSystem;

    public AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;

    public void Initialize()
    {
        Apply(ReadSystemTheme());

        if (_isFollowingSystem)
            return;

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _isFollowingSystem = true;
    }

    public void Dispose()
    {
        // SystemEvents is static: always unsubscribe.
        if (_isFollowingSystem)
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _isFollowingSystem = false;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // Switching the app mode raises the General category.
        if (e.Category != UserPreferenceCategory.General)
            return;

        var theme = ReadSystemTheme();
        Application.Current?.Dispatcher.InvokeAsync(() => Apply(theme));
    }

    private void Apply(AppTheme theme)
    {
        var resources = Application.Current?.Resources;
        if (resources is null)
            return;

        var source = new Uri(
            theme == AppTheme.Light ? "Themes/Colors/ClaudeLight.xaml" : "Themes/Colors/ClaudeDark.xaml",
            UriKind.Relative);

        var dictionaries = resources.MergedDictionaries;
        // The palette is always the first merged dictionary (see App.xaml).
        if (dictionaries.Count > 0 && dictionaries[0].Source is { } current && current.OriginalString.Contains("/Colors/"))
        {
            if (CurrentTheme == theme && current.OriginalString == source.OriginalString)
                return;
            dictionaries[0] = new ResourceDictionary { Source = source };
        }
        else
        {
            dictionaries.Insert(0, new ResourceDictionary { Source = source });
        }

        CurrentTheme = theme;
    }

    /// <summary><c>AppsUseLightTheme</c> = 0 means dark mode; missing (old Windows 10) means light.</summary>
    private static AppTheme ReadSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0 ? AppTheme.Dark : AppTheme.Light;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return AppTheme.Dark;
        }
    }
}
