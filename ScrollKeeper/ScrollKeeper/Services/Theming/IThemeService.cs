namespace ScrollKeeper.Services.Theming;

public enum AppTheme
{
    Dark,
    Light
}

/// <summary>
/// Applies the Claude palette (Themes/Colors/Claude*.xaml). Like Claude for desktop, it follows the
/// Windows app mode (Settings → Personalization → Colors) and switches live when it changes.
/// </summary>
public interface IThemeService
{
    AppTheme CurrentTheme { get; }

    /// <summary>Applies the theme that matches the Windows app mode and starts following it.</summary>
    void Initialize();
}
