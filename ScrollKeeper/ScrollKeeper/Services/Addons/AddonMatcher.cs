using System.Globalization;
using System.Text.RegularExpressions;
using ScrollKeeper.Services.Catalog;

namespace ScrollKeeper.Services.Addons;

/// <summary>An installed addon: one ESOUI entry (with all of its folders) or a folder ESOUI does not know.</summary>
/// <param name="Key">ESOUI id, or <c>local:FolderName</c> for unknown folders.</param>
/// <param name="Catalog">The matching ESOUI entry, if any.</param>
/// <param name="Folders">Installed folders that belong to it (the main folder first).</param>
/// <param name="InstalledVersion">Installed version: the recorded ESOUI version, otherwise the manifest's.</param>
/// <param name="HasUpdate">ESOUI has a newer version.</param>
public sealed record InstalledAddon(
    string Key,
    CatalogAddon? Catalog,
    IReadOnlyList<LocalAddon> Folders,
    string InstalledVersion,
    bool HasUpdate)
{
    public LocalAddon MainFolder => Folders[0];
}

/// <summary>Matches the folders in AddOns to ESOUI entries and decides which have updates.</summary>
public static partial class AddonMatcher
{
    public const string LocalKeyPrefix = "local:";

    public static IReadOnlyList<InstalledAddon> Match(
        IReadOnlyList<LocalAddon> localAddons,
        IReadOnlyList<CatalogAddon> catalog,
        IReadOnlyDictionary<string, InstallRecord> records)
    {
        var catalogById = new Dictionary<string, CatalogAddon>(StringComparer.Ordinal);
        var byFolder = new Dictionary<string, List<CatalogAddon>>(StringComparer.OrdinalIgnoreCase);
        foreach (var addon in catalog)
        {
            catalogById.TryAdd(addon.Id, addon);
            foreach (var dir in addon.Directories)
            {
                if (!byFolder.TryGetValue(dir, out var list))
                    byFolder[dir] = list = [];
                list.Add(addon);
            }
        }

        // Folders installed by ScrollKeeper belong to the addon that installed them.
        var recordedOwner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records.Values.OrderBy(r => r.InstalledAt))
        {
            if (!catalogById.ContainsKey(record.AddonId))
                continue;
            foreach (var dir in record.Directories)
                recordedOwner[dir] = record.AddonId;
        }

        var groups = new Dictionary<string, List<LocalAddon>>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var local in localAddons)
        {
            var key = recordedOwner.GetValueOrDefault(local.FolderName)
                      ?? FindOwner(local.FolderName, byFolder)?.Id
                      ?? LocalKeyPrefix + local.FolderName;

            if (!groups.TryGetValue(key, out var folders))
            {
                groups[key] = folders = [];
                order.Add(key);
            }

            folders.Add(local);
        }

        var result = new List<InstalledAddon>(order.Count);
        foreach (var key in order)
        {
            var folders = groups[key];
            var addon = catalogById.GetValueOrDefault(key);

            if (addon is not null && addon.Directories.Count > 0)
            {
                // Main folder (the first one ESOUI lists) first.
                var main = folders.FindIndex(f => string.Equals(f.FolderName, addon.Directories[0], StringComparison.OrdinalIgnoreCase));
                if (main > 0)
                    (folders[0], folders[main]) = (folders[main], folders[0]);
            }

            var record = addon is null ? null : records.GetValueOrDefault(addon.Id);
            var installedVersion = !string.IsNullOrWhiteSpace(record?.Version)
                ? record.Version
                : folders[0].Manifest.Version;

            var hasUpdate = addon is not null && IsUpdateAvailable(addon.Version, installedVersion, isRecorded: record is not null);
            result.Add(new InstalledAddon(key, addon, folders, installedVersion, hasUpdate));
        }

        return result;
    }

    /// <summary>
    /// Picks the ESOUI entry a folder belongs to: preferably one whose main folder it is
    /// (a library bundled with many addons usually also has its own entry), the most downloaded on ties.
    /// </summary>
    private static CatalogAddon? FindOwner(string folder, Dictionary<string, List<CatalogAddon>> byFolder)
    {
        if (!byFolder.TryGetValue(folder, out var candidates))
            return null;

        return candidates
            .OrderByDescending(c => string.Equals(c.Directories[0], folder, StringComparison.OrdinalIgnoreCase))
            .ThenBy(c => c.Directories.Count)
            .ThenByDescending(c => c.Downloads)
            .First();
    }

    /// <summary>
    /// Whether <paramref name="latestVersion"/> (ESOUI) is newer than <paramref name="installedVersion"/>.
    /// A version recorded at install time is ESOUI's own string, so any difference is an update.
    /// A manifest version is free text: compare the numbers in it, and treat an unreadable difference as an update.
    /// </summary>
    public static bool IsUpdateAvailable(string latestVersion, string installedVersion, bool isRecorded)
    {
        var latest = Normalize(latestVersion);
        var installed = Normalize(installedVersion);

        if (latest.Length == 0 || installed.Length == 0)
            return false;

        if (string.Equals(latest, installed, StringComparison.OrdinalIgnoreCase))
            return false;

        if (isRecorded)
            return true;

        var comparison = CompareNumbers(latest, installed);
        return comparison is null || comparison > 0;
    }

    /// <summary>
    /// Compares the first dotted number of two versions ("v1.2.10" vs "1.2.9" → 1).
    /// <c>null</c> if either has no number.
    /// </summary>
    public static int? CompareNumbers(string left, string right)
    {
        var a = NumberRegex().Match(left);
        var b = NumberRegex().Match(right);
        if (!a.Success || !b.Success)
            return null;

        var aParts = a.Value.Split('.');
        var bParts = b.Value.Split('.');
        for (var i = 0; i < Math.Max(aParts.Length, bParts.Length); i++)
        {
            var x = i < aParts.Length ? ParsePart(aParts[i]) : 0;
            var y = i < bParts.Length ? ParsePart(bParts[i]) : 0;
            if (x != y)
                return x.CompareTo(y);
        }

        return 0;
    }

    private static decimal ParsePart(string part) =>
        decimal.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static string Normalize(string? version)
    {
        var text = AddonManifest.StripFormatting(version);
        if (text.Length > 1 && (text[0] == 'v' || text[0] == 'V') && char.IsAsciiDigit(text[1]))
            text = text[1..];
        return text;
    }

    [GeneratedRegex(@"\d+(?:\.\d+)*")]
    private static partial Regex NumberRegex();
}
