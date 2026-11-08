using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace ScrollKeeper.Services.Addons;

/// <summary>
/// The <c>## Key: Value</c> header of an addon manifest (<c>Folder\Folder.addon</c> or <c>Folder\Folder.txt</c>).
/// </summary>
public sealed partial record AddonManifest(
    string Title,
    string Author,
    string Version,
    int AddOnVersion,
    bool IsLibrary,
    string Description)
{
    /// <summary>Manifest files are small; stop reading suspiciously large ones.</summary>
    private const int MaxLines = 400;

    public static AddonManifest Parse(IEnumerable<string> lines, string fallbackTitle)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in lines.Take(MaxLines))
        {
            var line = raw.Trim();
            if (!line.StartsWith("##", StringComparison.Ordinal))
                continue;

            var separator = line.IndexOf(':');
            if (separator < 0)
                continue;

            var key = line[2..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            // The first occurrence wins, like in the game.
            if (key.Length > 0)
                headers.TryAdd(key, value);
        }

        var title = StripFormatting(headers.GetValueOrDefault("Title"));
        int.TryParse(headers.GetValueOrDefault("AddOnVersion"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var addOnVersion);

        return new AddonManifest(
            string.IsNullOrWhiteSpace(title) ? fallbackTitle : title,
            StripFormatting(headers.GetValueOrDefault("Author")),
            StripFormatting(headers.GetValueOrDefault("Version")),
            addOnVersion,
            string.Equals(headers.GetValueOrDefault("IsLibrary"), "true", StringComparison.OrdinalIgnoreCase),
            StripFormatting(headers.GetValueOrDefault("Description")));
    }

    /// <summary>Reads the manifest of an addon folder, or returns <c>null</c> if the folder is not an addon.</summary>
    public static AddonManifest? TryRead(string folderPath)
    {
        var name = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        // Since Update 41 the game prefers .addon over .txt when both exist.
        foreach (var extension in (string[])[".addon", ".txt"])
        {
            var path = Path.Combine(folderPath, name + extension);
            if (!File.Exists(path))
                continue;

            try
            {
                return Parse(File.ReadLines(path), name);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>Removes ESO markup from manifest values: <c>|cRRGGBB</c> / <c>|r</c> colors and <c>|t...|t</c> textures.</summary>
    public static string StripFormatting(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var text = TextureRegex().Replace(value, string.Empty);
        text = ColorRegex().Replace(text, string.Empty);
        return WhitespaceRegex().Replace(text, " ").Trim();
    }

    [GeneratedRegex(@"\|t[^|]*\|t", RegexOptions.IgnoreCase)]
    private static partial Regex TextureRegex();

    [GeneratedRegex(@"\|c[0-9a-fA-F]{6}|\|r", RegexOptions.IgnoreCase)]
    private static partial Regex ColorRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
