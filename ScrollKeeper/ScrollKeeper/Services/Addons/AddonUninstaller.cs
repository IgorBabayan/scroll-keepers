using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ScrollKeeper.Services.Addons;

public sealed partial class AddonUninstaller(IInstallRecordStore records) : IAddonUninstaller
{
    public RemovalPlan Plan(string addonsFolder, InstalledAddon addon)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(addonsFolder);
        ArgumentNullException.ThrowIfNull(addon);

        var folders = ScanFolders(addonsFolder);
        var ownFolders = addon.Folders.Select(f => f.FolderName).ToList();
        var dependencyInstalled = records.GetAll(addonsFolder).Values
            .Where(r => r.IsDependency)
            .SelectMany(r => r.Directories)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var plan = Plan(folders, addon.MainFolder.FolderName, ownFolders, dependencyInstalled, SavedVariablesFolder(addonsFolder));
        return plan with { AddonId = addon.Catalog?.Id };
    }

    /// <summary>Pure part of <see cref="Plan(string, InstalledAddon)"/> (internal for tests).</summary>
    internal static RemovalPlan Plan(
        IReadOnlyDictionary<string, FolderInfo> folders,
        string mainFolder,
        IReadOnlyList<string> ownFolders,
        IReadOnlySet<string> dependencyInstalled,
        string? savedVariablesFolder)
    {
        var own = new HashSet<string>(ownFolders, StringComparer.OrdinalIgnoreCase);
        var removal = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { mainFolder };

        // The addon's other folders and everything it requires, followed through the libraries' own requirements.
        var candidates = new List<string>(ownFolders.Where(f => !removal.Contains(f)));
        if (folders.TryGetValue(mainFolder, out var main))
            AddRange(candidates, main.Requires);

        bool changed;
        do
        {
            changed = false;
            var used = UsedNames(folders, removal);

            for (var i = 0; i < candidates.Count; i++)
            {
                var name = candidates[i];
                if (removal.Contains(name) || used.Contains(name) || !folders.TryGetValue(name, out var info))
                    continue;

                // Only libraries go with the addon: a regular addon stays even if nothing requires it any more.
                if (!own.Contains(name) && !info.IsLibrary && !dependencyInstalled.Contains(name))
                    continue;

                removal.Add(name);
                AddRange(candidates, info.Requires);
                changed = true;
            }
        } while (changed);

        var addonFolders = ownFolders.Where(removal.Contains).ToList();
        if (!addonFolders.Contains(mainFolder, StringComparer.OrdinalIgnoreCase))
            addonFolders.Insert(0, mainFolder);

        var unused = removal.Where(f => !own.Contains(f)).Order(StringComparer.OrdinalIgnoreCase).ToList();
        var kept = candidates
            .Where(c => !removal.Contains(c) && folders.ContainsKey(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Who still needs what is removed (the user may remove a library on purpose).
        var removedNames = removal
            .SelectMany(f => folders.TryGetValue(f, out var info) ? info.Provides : new HashSet<string> { f })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var remaining = folders.Values.Where(f => !removal.Contains(f.Name)).ToList();
        var stillProvided = remaining.SelectMany(f => f.Provides).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var requiredBy = remaining
            .Where(f => f.Requires.Any(r => removedNames.Contains(r) && !stillProvided.Contains(r)))
            .Select(f => f.Title)
            .Order(StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        // Each addon (nested libraries too) keeps its settings in SavedVariables\<FolderName>.lua.
        var savedVariables = new List<string>();
        if (savedVariablesFolder is not null)
        {
            foreach (var name in removedNames.Where(n => !stillProvided.Contains(n)).Order(StringComparer.OrdinalIgnoreCase))
            {
                var path = Path.Combine(savedVariablesFolder, name + ".lua");
                if (File.Exists(path))
                    savedVariables.Add(path);
            }
        }

        // The same globals (## SavedVariables: in the manifests) can also sit in other files, e.g. left by
        // an older or renamed copy of the addon. Globals another remaining addon also declares stay.
        var cleanups = new List<SavedVariablesCleanup>();
        if (savedVariablesFolder is not null)
        {
            var removedGlobals = removal
                .Where(folders.ContainsKey)
                .SelectMany(f => folders[f].SavedVariables)
                .ToHashSet(StringComparer.Ordinal);
            removedGlobals.ExceptWith(remaining.SelectMany(f => f.SavedVariables));

            if (removedGlobals.Count > 0)
                FindSavedVariablesGlobals(savedVariablesFolder, removedGlobals, savedVariables, cleanups);
        }

        return new RemovalPlan(null, addonFolders, unused, kept, savedVariables, cleanups, requiredBy);
    }

    /// <summary>
    /// Looks through every <c>SavedVariables\*.lua</c> not already deleted for top-level assignments of
    /// <paramref name="globals"/>: a file holding nothing else is deleted whole, otherwise only those globals are cut out.
    /// </summary>
    internal static void FindSavedVariablesGlobals(
        string savedVariablesFolder,
        IReadOnlySet<string> globals,
        List<string> filesToDelete,
        List<SavedVariablesCleanup> cleanups)
    {
        if (!Directory.Exists(savedVariablesFolder))
            return;

        var deleted = filesToDelete.ToHashSet(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(savedVariablesFolder, "*.lua").Order(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Could not list {savedVariablesFolder}: {ex.Message}");
            return;
        }

        foreach (var file in files)
        {
            // The game's own settings (ZO_Ingame.lua, ZO_Pregame.lua, ...) are never touched.
            if (deleted.Contains(file) || Path.GetFileName(file).StartsWith("ZO_", StringComparison.OrdinalIgnoreCase))
                continue;

            List<string> declared;
            try
            {
                declared = ReadTopLevelGlobals(File.ReadLines(file));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"Could not read {file}: {ex.Message}");
                continue;
            }

            var matched = declared.Where(globals.Contains).Distinct(StringComparer.Ordinal).ToList();
            if (matched.Count == 0)
                continue;

            if (declared.All(globals.Contains))
                filesToDelete.Add(file);
            else
                cleanups.Add(new SavedVariablesCleanup(file, matched));
        }
    }

    /// <summary>Names assigned at the top level of a saved-variables file (<c>Name =</c> at the start of a line).</summary>
    internal static List<string> ReadTopLevelGlobals(IEnumerable<string> lines)
    {
        var result = new List<string>();
        foreach (var line in lines)
        {
            // The game writes the table contents indented, so only top-level assignments start with a name.
            if (line.Length == 0 || !(char.IsAsciiLetter(line[0]) || line[0] == '_'))
                continue;

            var match = TopLevelAssignmentRegex().Match(line);
            if (match.Success)
                result.Add(match.Groups[1].Value);
        }

        return result;
    }

    /// <summary>
    /// Cuts the top-level assignments of <paramref name="globals"/> out of a saved-variables file's text;
    /// each one runs up to the next top-level assignment (or the end of the file).
    /// </summary>
    internal static string StripGlobals(string text, IReadOnlySet<string> globals)
    {
        var builder = new StringBuilder(text.Length);
        var skipping = false;
        var start = 0;

        while (start < text.Length)
        {
            var end = text.IndexOf('\n', start);
            end = end < 0 ? text.Length : end + 1;
            var line = text[start..end];

            if (line.Length > 0 && (char.IsAsciiLetter(line[0]) || line[0] == '_'))
            {
                var match = TopLevelAssignmentRegex().Match(line.TrimEnd('\r', '\n'));
                if (match.Success)
                    skipping = globals.Contains(match.Groups[1].Value);
            }

            if (!skipping)
                builder.Append(line);
            start = end;
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"^([A-Za-z_][A-Za-z0-9_]*)\s*=")]
    private static partial Regex TopLevelAssignmentRegex();

    public void Remove(string addonsFolder, RemovalPlan plan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(addonsFolder);
        ArgumentNullException.ThrowIfNull(plan);

        var root = Path.GetFullPath(addonsFolder);
        var removed = new List<string>();

        try
        {
            foreach (var folder in plan.AllFolders)
            {
                // Plain folder names only: nothing outside AddOns can be deleted.
                if (folder.Length == 0 || folder.IndexOfAny(['/', '\\', ':']) >= 0 || folder is "." or "..")
                    continue;

                var path = Path.Combine(root, folder);
                if (Directory.Exists(path))
                    AddonInstaller.DeleteDirectory(path);
                removed.Add(folder);
            }
        }
        finally
        {
            // Also after a failure, so the records match what is left on disk.
            records.RemoveDirectories(addonsFolder, removed, plan.AddonId);
        }

        foreach (var file in plan.SavedVariablesFiles)
        {
            try
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The addon is gone; a settings file the game still holds open is not worth failing for.
                Debug.WriteLine($"Could not delete {file}: {ex.Message}");
            }
        }

        foreach (var cleanup in plan.SavedVariablesCleanups)
        {
            var temp = cleanup.File + ".scrollkeeper.tmp";
            try
            {
                if (!File.Exists(cleanup.File))
                    continue;

                var text = File.ReadAllText(cleanup.File);
                var stripped = StripGlobals(text, cleanup.Globals.ToHashSet(StringComparer.Ordinal));
                if (stripped.Length == text.Length)
                    continue;

                // Written beside the original and swapped in, so a failure never leaves a half-written file.
                File.WriteAllText(temp, stripped, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                File.Move(temp, cleanup.File, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"Could not clean {cleanup.File}: {ex.Message}");
                try
                {
                    if (File.Exists(temp))
                        File.Delete(temp);
                }
                catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException)
                {
                    Debug.WriteLine($"Could not delete {temp}: {cleanupEx.Message}");
                }
            }
        }
    }

    // ===== AddOns folder =====

    /// <summary>A top-level folder of AddOns with what it provides and needs (its nested libraries included).</summary>
    /// <param name="Name">Folder name.</param>
    /// <param name="Title">Display title (the manifest title, or the folder name).</param>
    /// <param name="IsLibrary">The folder's own manifest says <c>## IsLibrary: true</c>.</param>
    /// <param name="Provides">Addon names loaded from it: the folder itself and the addons nested in it.</param>
    /// <param name="Requires">Required dependencies of all those addons.</param>
    /// <param name="Uses">Required and optional dependencies of all those addons.</param>
    /// <param name="SavedVariables">Saved-variable globals declared by all those addons (<c>## SavedVariables:</c>).</param>
    internal sealed record FolderInfo(
        string Name,
        string Title,
        bool IsLibrary,
        IReadOnlySet<string> Provides,
        IReadOnlySet<string> Requires,
        IReadOnlySet<string> Uses,
        IReadOnlySet<string> SavedVariables);

    internal static Dictionary<string, FolderInfo> ScanFolders(string addonsFolder)
    {
        var result = new Dictionary<string, FolderInfo>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(addonsFolder))
            return result;

        foreach (var path in Directory.EnumerateDirectories(addonsFolder))
        {
            var name = Path.GetFileName(path);
            if (name.StartsWith('.'))
                continue;

            var manifests = new List<(string Folder, AddonManifest Manifest)>();
            DependencyResolver.CollectManifests(path, depth: 1, manifests);
            if (manifests.Count == 0)
                continue;

            AddonManifest? own = manifests.FirstOrDefault(m => string.Equals(m.Folder, name, StringComparison.OrdinalIgnoreCase)).Manifest;
            var provides = manifests.Select(m => m.Folder).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var requires = manifests.SelectMany(m => m.Manifest.DependsOn).Select(d => d.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var uses = manifests.SelectMany(m => m.Manifest.OptionalDependsOn).Select(d => d.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            uses.UnionWith(requires);
            var savedVariables = manifests.SelectMany(m => m.Manifest.SavedVariables).ToHashSet(StringComparer.Ordinal);

            result[name] = new FolderInfo(name, own?.Title ?? name, own?.IsLibrary == true, provides, requires, uses, savedVariables);
        }

        return result;
    }

    /// <summary>Names required or optionally used by the folders that stay (a folder's use of itself does not count).</summary>
    private static HashSet<string> UsedNames(IReadOnlyDictionary<string, FolderInfo> folders, HashSet<string> removal)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders.Values)
        {
            if (removal.Contains(folder.Name))
                continue;

            foreach (var name in folder.Uses)
            {
                if (!folder.Provides.Contains(name))
                    used.Add(name);
            }
        }

        return used;
    }

    /// <summary><c>live\SavedVariables</c> next to <c>live\AddOns</c>.</summary>
    private static string? SavedVariablesFolder(string addonsFolder)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(addonsFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return parent is null ? null : Path.Combine(parent, "SavedVariables");
    }

    private static void AddRange(List<string> target, IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            if (!target.Contains(name, StringComparer.OrdinalIgnoreCase))
                target.Add(name);
        }
    }
}
