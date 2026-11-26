namespace ScrollKeeper.Services.Settings;

/// <summary>Default locations of The Elder Scrolls Online user folders.</summary>
public static class EsoPaths
{
    /// <summary>
    /// The game keeps one user folder per megaserver: <c>live</c> (NA) and <c>liveeu</c> (EU).
    /// Both use the same AddOns layout.
    /// </summary>
    private static readonly string[] ServerFolders = ["live", "liveeu"];

    /// <summary>Steam app id of The Elder Scrolls Online (name of its Proton prefix folder).</summary>
    private const string SteamAppId = "306130";

    private const string GameFolderName = "Elder Scrolls Online";

    /// <summary>
    /// <c>Documents\Elder Scrolls Online</c> (follows OneDrive folder redirection on Windows).
    /// On Linux, where the game runs through Steam Proton or Wine, the first such folder that exists in a known prefix.
    /// </summary>
    public static string GameDocumentsFolder =>
        DocumentsFolders().Select(d => Path.Combine(d, GameFolderName)).FirstOrDefault(Directory.Exists)
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), GameFolderName);

    /// <summary>
    /// The first existing <c>AddOns</c> folder of the known servers, otherwise the NA (<c>live</c>) one.
    /// </summary>
    public static string FindAddonsFolder()
    {
        foreach (var documents in DocumentsFolders())
        {
            foreach (var server in ServerFolders)
            {
                var candidate = Path.Combine(documents, GameFolderName, server, "AddOns");
                if (Directory.Exists(candidate))
                    return candidate;
            }
        }

        return Path.Combine(GameDocumentsFolder, ServerFolders[0], "AddOns");
    }

    /// <summary>
    /// Documents folders the game may write to: the user's own (Windows, macOS, and Wine setups that link it), then on
    /// Linux the <c>Documents</c> folder of the Steam Proton prefix (native and Flatpak Steam) and of the default Wine prefix.
    /// </summary>
    private static IEnumerable<string> DocumentsFolders()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        if (!OperatingSystem.IsLinux())
            yield break;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] steamRoots =
        [
            Path.Combine(home, ".local", "share", "Steam"),
            Path.Combine(home, ".steam", "steam"),
            Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam")
        ];

        foreach (var steam in steamRoots)
            yield return Path.Combine(steam, "steamapps", "compatdata", SteamAppId, "pfx", "drive_c", "users", "steamuser", "Documents");

        var user = Environment.UserName;
        yield return Path.Combine(home, ".wine", "drive_c", "users", user, "Documents");
        yield return Path.Combine(home, ".wine", "drive_c", "users", user, "My Documents");
    }
}
