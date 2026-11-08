using System.IO;

namespace ScrollKeeper.Services.Settings;

/// <summary>Folders where ScrollKeeper keeps its own files.</summary>
public static class AppPaths
{
    /// <summary><c>%APPDATA%\ScrollKeeper</c>: settings and the list of addons installed by ScrollKeeper.</summary>
    public static string RoamingFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScrollKeeper");

    /// <summary><c>%LOCALAPPDATA%\ScrollKeeper\Cache</c>: catalog and image cache, safe to delete.</summary>
    public static string CacheFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScrollKeeper", "Cache");

    /// <summary><c>%TEMP%\ScrollKeeper</c>: downloads in progress.</summary>
    public static string TempFolder { get; } = Path.Combine(Path.GetTempPath(), "ScrollKeeper");
}
