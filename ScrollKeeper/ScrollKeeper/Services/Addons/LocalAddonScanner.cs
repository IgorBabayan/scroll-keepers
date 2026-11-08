using System.IO;

namespace ScrollKeeper.Services.Addons;

public sealed class LocalAddonScanner : ILocalAddonScanner
{
    public IReadOnlyList<LocalAddon> Scan(string addonsFolder)
    {
        if (string.IsNullOrWhiteSpace(addonsFolder) || !Directory.Exists(addonsFolder))
            return [];

        var result = new List<LocalAddon>();

        IEnumerable<string> folders;
        try
        {
            folders = Directory.EnumerateDirectories(addonsFolder).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        foreach (var folder in folders)
        {
            var name = Path.GetFileName(folder);
            // Our own staging folders and hidden folders are not addons.
            if (name.StartsWith('.'))
                continue;

            var manifest = AddonManifest.TryRead(folder);
            if (manifest is not null)
                result.Add(new LocalAddon(name, folder, manifest));
        }

        result.Sort((a, b) => string.Compare(a.Manifest.Title, b.Manifest.Title, StringComparison.CurrentCultureIgnoreCase));
        return result;
    }
}
