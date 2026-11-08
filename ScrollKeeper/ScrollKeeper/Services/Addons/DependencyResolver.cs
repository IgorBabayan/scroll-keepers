using System.IO;
using ScrollKeeper.Services.Catalog;

namespace ScrollKeeper.Services.Addons;

public sealed class DependencyResolver : IDependencyResolver
{
    /// <summary>The game loads addons nested up to three folders below AddOns (bundled libraries).</summary>
    internal const int MaxDepth = 3;

    public IReadOnlyList<MissingDependency> FindMissing(
        string addonsFolder,
        IReadOnlyList<string> directories,
        IReadOnlyList<CatalogAddon> catalog)
    {
        if (string.IsNullOrWhiteSpace(addonsFolder) || !Directory.Exists(addonsFolder))
            return [];

        var available = ScanAvailable(addonsFolder);

        // Manifests of the installed folders and of the libraries bundled inside them.
        var manifests = new List<(string Folder, AddonManifest Manifest)>();
        foreach (var directory in directories)
        {
            var path = Path.Combine(addonsFolder, directory);
            if (Directory.Exists(path))
                CollectManifests(path, depth: 1, manifests);
        }

        return Resolve(manifests, available, catalog);
    }

    /// <summary>Pure part of <see cref="FindMissing"/> (internal for tests).</summary>
    internal static IReadOnlyList<MissingDependency> Resolve(
        IEnumerable<(string Folder, AddonManifest Manifest)> manifests,
        IReadOnlyDictionary<string, int> available,
        IReadOnlyList<CatalogAddon> catalog)
    {
        var result = new List<MissingDependency>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<CatalogAddon>>? byFolder = null;

        foreach (var (folder, manifest) in manifests)
        {
            foreach (var dependency in manifest.DependsOn)
            {
                int? installedVersion = null;
                if (available.TryGetValue(dependency.Name, out var version))
                {
                    if (dependency.MinAddOnVersion is not { } min || version >= min)
                        continue;
                    installedVersion = version;
                }

                if (!seen.Add(dependency.Name))
                    continue;

                byFolder ??= IndexByFolder(catalog);
                result.Add(new MissingDependency(dependency, folder, installedVersion, FindSource(dependency.Name, byFolder)));
            }
        }

        return result;
    }

    /// <summary>
    /// Every addon the game would load from AddOns, with its <c>## AddOnVersion</c>.
    /// When the same name exists several times the game uses the newest copy, so the highest version is kept.
    /// </summary>
    internal static Dictionary<string, int> ScanAvailable(string addonsFolder)
    {
        var manifests = new List<(string Folder, AddonManifest Manifest)>();
        foreach (var directory in EnumerateDirectories(addonsFolder))
            CollectManifests(directory, depth: 1, manifests);

        var available = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (folder, manifest) in manifests)
        {
            if (!available.TryGetValue(folder, out var existing) || manifest.AddOnVersion > existing)
                available[folder] = manifest.AddOnVersion;
        }

        return available;
    }

    private static void CollectManifests(string path, int depth, List<(string Folder, AddonManifest Manifest)> target)
    {
        var name = Path.GetFileName(path);
        if (name.StartsWith('.'))
            return;

        if (AddonManifest.TryRead(path) is { } manifest)
            target.Add((name, manifest));

        if (depth >= MaxDepth)
            return;

        foreach (var child in EnumerateDirectories(path))
            CollectManifests(child, depth + 1, target);
    }

    private static IEnumerable<string> EnumerateDirectories(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static Dictionary<string, List<CatalogAddon>> IndexByFolder(IReadOnlyList<CatalogAddon> catalog)
    {
        var byFolder = new Dictionary<string, List<CatalogAddon>>(StringComparer.OrdinalIgnoreCase);
        foreach (var addon in catalog)
        {
            foreach (var directory in addon.Directories)
            {
                if (!byFolder.TryGetValue(directory, out var list))
                    byFolder[directory] = list = [];
                list.Add(addon);
            }
        }

        return byFolder;
    }

    /// <summary>
    /// The ESOUI entry to install for a dependency: preferably the library's own entry (its main folder),
    /// then the smallest one that contains it, the most downloaded on ties.
    /// </summary>
    private static CatalogAddon? FindSource(string name, Dictionary<string, List<CatalogAddon>> byFolder)
    {
        if (!byFolder.TryGetValue(name, out var candidates))
            return null;

        return candidates
            .OrderByDescending(c => string.Equals(c.Directories[0], name, StringComparison.OrdinalIgnoreCase))
            .ThenBy(c => c.Directories.Count)
            .ThenByDescending(c => c.Downloads)
            .First();
    }
}
