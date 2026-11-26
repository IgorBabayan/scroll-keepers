namespace ScrollKeeper.Services.Theming;

public enum AppTheme
{
    Dark,
    Light
}

/// <summary>
/// Applies the Claude palette (Themes/Colors/Claude*.axaml). Like Claude for desktop, it follows the
/// operating system's light / dark mode (Windows app mode, macOS appearance, the Linux desktop's color scheme)
/// and switches live when it changes.
/// </summary>
public interface IThemeService
{
    AppTheme CurrentTheme { get; }

    /// <summary>Applies the theme that matches the system mode and starts following it.</summary>
    void Initialize();
}
