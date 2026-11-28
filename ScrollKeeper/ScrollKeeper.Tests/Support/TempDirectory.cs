namespace ScrollKeeper.Tests.Support;

/// <summary>A unique folder under the system temp folder, deleted on dispose.</summary>
public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ScrollKeeper.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    /// <summary>Creates <c>relativeFolder\&lt;name&gt;.txt</c> (the addon manifest) with the given lines.</summary>
    public string WriteManifest(string relativeFolder, params string[] lines) =>
        WriteManifestFile(relativeFolder, ".txt", lines);

    /// <summary>Creates <c>relativeFolder\&lt;name&gt;&lt;extension&gt;</c> (<c>.txt</c> or <c>.addon</c>) with the given lines.</summary>
    public string WriteManifestFile(string relativeFolder, string extension, params string[] lines)
    {
        var folder = System.IO.Path.Combine(Path, relativeFolder);
        Directory.CreateDirectory(folder);
        var file = System.IO.Path.Combine(folder, System.IO.Path.GetFileName(folder) + extension);
        File.WriteAllLines(file, lines);
        return folder;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: the OS cleans the temp folder eventually.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
