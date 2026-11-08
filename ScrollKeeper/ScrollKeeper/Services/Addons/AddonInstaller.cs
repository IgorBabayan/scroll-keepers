using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using ScrollKeeper.Services.Catalog;
using ScrollKeeper.Services.Settings;

namespace ScrollKeeper.Services.Addons;

public sealed class AddonInstaller(IAddonCatalog catalog, HttpClient http, IInstallRecordStore records) : IAddonInstaller
{
    private static readonly TimeSpan DetailsTimeout = TimeSpan.FromSeconds(30);

    public async Task<InstallRecord> InstallAsync(
        CatalogAddon addon,
        string addonsFolder,
        IProgress<InstallProgress>? progress,
        bool asDependency = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(addon);
        ArgumentException.ThrowIfNullOrWhiteSpace(addonsFolder);

        progress?.Report(new InstallProgress(InstallStage.Downloading, null));

        AddonDetails details;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(DetailsTimeout);
            details = await catalog.GetDetailsAsync(addon.Id, timeout.Token).ConfigureAwait(false);
        }

        if (!Uri.TryCreate(details.DownloadUrl, UriKind.Absolute, out var downloadUrl))
            throw new InvalidDataException($"ESOUI has no download for \"{addon.Name}\".");

        var work = Path.Combine(AppPaths.TempFolder, "installs", $"{addon.Id}-{Guid.NewGuid():N}");
        var archivePath = Path.Combine(work, "addon.zip");
        var stagingPath = Path.Combine(work, "staging");

        try
        {
            Directory.CreateDirectory(work);
            await DownloadAsync(downloadUrl, archivePath, progress, cancellationToken).ConfigureAwait(false);

            progress?.Report(new InstallProgress(InstallStage.Installing, 0));

            var directories = await Task.Run(() =>
            {
                var extracted = ExtractToStaging(archivePath, stagingPath, progress, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                // From here on the addon's folders are replaced: no cancellation in the middle.
                ReplaceFolders(stagingPath, extracted, addonsFolder);
                return extracted;
            }, cancellationToken).ConfigureAwait(false);

            progress?.Report(new InstallProgress(InstallStage.Installing, 1));

            var version = string.IsNullOrWhiteSpace(details.Version) ? addon.Version : details.Version;
            var previous = records.GetAll(addonsFolder).GetValueOrDefault(addon.Id);
            var isDependency = previous?.IsDependency ?? asDependency;
            var record = new InstallRecord(addon.Id, version, directories, DateTimeOffset.UtcNow, isDependency);
            records.Save(addonsFolder, record);
            return record;
        }
        finally
        {
            TryDeleteDirectory(work);
        }
    }

    private async Task DownloadAsync(Uri url, string targetPath, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? 0;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        var buffer = new byte[81920];
        long received = 0;
        int read;
        var lastReport = Stopwatch.StartNew();
        progress?.Report(new InstallProgress(InstallStage.Downloading, total > 0 ? 0 : null, 0, total));

        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;

            // Throttle reports: progress is marshalled to the UI thread.
            if (lastReport.ElapsedMilliseconds >= 50)
            {
                progress?.Report(new InstallProgress(InstallStage.Downloading, Fraction(received, total), received, total));
                lastReport.Restart();
            }
        }

        progress?.Report(new InstallProgress(InstallStage.Downloading, 1, received, Math.Max(total, received)));
    }

    /// <summary>
    /// Extracts the archive into <paramref name="stagingPath"/> and returns its top-level folders
    /// (the folders that go into AddOns). Entries that would land outside the staging folder are rejected.
    /// </summary>
    internal static IReadOnlyList<string> ExtractToStaging(
        string archivePath,
        string stagingPath,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(stagingPath);
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(root);

        using var archive = ZipFile.OpenRead(archivePath);
        var entries = archive.Entries;
        var topLevel = new List<string>();

        for (var i = 0; i < entries.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entry = entries[i];
            var name = entry.FullName.Replace('\\', '/').TrimStart('/');
            var separator = name.IndexOf('/');

            // Loose files next to the addon folders (readme, license) are not installed.
            if (separator <= 0)
                continue;

            var top = name[..separator];
            if (top is "." or ".." || top.StartsWith("__MACOSX", StringComparison.OrdinalIgnoreCase))
                continue;

            var destination = Path.GetFullPath(Path.Combine(root, name));
            if (!destination.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The archive contains an unsafe path: {entry.FullName}");

            if (!topLevel.Contains(top, StringComparer.OrdinalIgnoreCase))
                topLevel.Add(top);

            if (name.EndsWith('/'))
            {
                Directory.CreateDirectory(destination);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
            }

            progress?.Report(new InstallProgress(InstallStage.Installing, 0.9 * (i + 1) / entries.Count));
        }

        if (topLevel.Count == 0)
            throw new InvalidDataException("The archive does not contain any addon folder.");

        return topLevel;
    }

    /// <summary>Replaces each folder in AddOns with its freshly extracted copy.</summary>
    internal static void ReplaceFolders(string stagingPath, IReadOnlyList<string> directories, string addonsFolder)
    {
        Directory.CreateDirectory(addonsFolder);
        var sameVolume = string.Equals(
            Path.GetPathRoot(Path.GetFullPath(stagingPath)),
            Path.GetPathRoot(Path.GetFullPath(addonsFolder)),
            StringComparison.OrdinalIgnoreCase);

        foreach (var directory in directories)
        {
            var source = Path.Combine(stagingPath, directory);
            var target = Path.Combine(addonsFolder, directory);

            if (Directory.Exists(target))
                DeleteDirectory(target);

            if (sameVolume && TryMove(source, target))
                continue;

            CopyDirectory(source, target);
        }
    }

    /// <summary>A move is instant, but fails across mount points that share a drive letter: copy then.</summary>
    private static bool TryMove(string source, string target)
    {
        try
        {
            Directory.Move(source, target);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);

        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);

        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
    }

    private const int DeleteAttempts = 5;

    /// <summary>
    /// Deletes a folder, including read-only files and folders (some archives carry that attribute, and OneDrive
    /// sets it on synced folders). Retries briefly: OneDrive, antivirus or Explorer often hold a folder for a moment.
    /// </summary>
    internal static void DeleteDirectory(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (!Directory.Exists(path))
                    return;

                ClearReadOnly(path);
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < DeleteAttempts)
            {
                Debug.WriteLine($"Delete of {path} failed (attempt {attempt}): {ex.Message}");
                Thread.Sleep(250 * attempt);
            }
        }
    }

    /// <summary>Windows refuses to delete a read-only file or folder: clear the flag on the folder and everything in it.</summary>
    private static void ClearReadOnly(string path)
    {
        var root = new DirectoryInfo(path);
        ClearReadOnly(root);

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = 0,
            IgnoreInaccessible = true,
        };

        foreach (var entry in root.EnumerateFileSystemInfos("*", options))
            ClearReadOnly(entry);
    }

    private static void ClearReadOnly(FileSystemInfo entry)
    {
        try
        {
            var attributes = entry.Attributes;
            if ((attributes & FileAttributes.ReadOnly) != 0)
                entry.Attributes = attributes & ~FileAttributes.ReadOnly;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Directory.Delete reports it if it still matters.
            Debug.WriteLine($"Could not clear read-only on {entry.FullName}: {ex.Message}");
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                DeleteDirectory(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Could not clean up {path}: {ex.Message}");
        }
    }

    private static double? Fraction(long received, long total) =>
        total > 0 ? Math.Clamp((double)received / total, 0, 1) : null;
}
