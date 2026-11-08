using System.IO;

namespace ScrollKeeper.Services.Settings;

/// <summary>Default locations of The Elder Scrolls Online user folders.</summary>
public static class EsoPaths
{
    /// <summary>
    /// The game keeps one user folder per megaserver: <c>live</c> (NA) and <c>liveeu</c> (EU).
    /// Both use the same AddOns layout.
    /// </summary>
    private static readonly string[] ServerFolders = ["live", "liveeu"];

    /// <summary><c>Documents\Elder Scrolls Online</c> (follows OneDrive folder redirection).</summary>
    public static string GameDocumentsFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Elder Scrolls Online");

    /// <summary>
    /// The first existing <c>AddOns</c> folder of the known servers, otherwise the NA (<c>live</c>) one.
    /// </summary>
    public static string FindAddonsFolder()
    {
        foreach (var server in ServerFolders)
        {
            var candidate = Path.Combine(GameDocumentsFolder, server, "AddOns");
            if (Directory.Exists(candidate))
                return candidate;
        }

        return Path.Combine(GameDocumentsFolder, ServerFolders[0], "AddOns");
    }
}
