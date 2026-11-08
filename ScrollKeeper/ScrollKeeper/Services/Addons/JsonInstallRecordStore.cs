using System.IO;
using System.Text.Json;
using ScrollKeeper.Services.Settings;

namespace ScrollKeeper.Services.Addons;

/// <summary>
/// Keeps the records in <c>%APPDATA%\ScrollKeeper\installed.json</c>, grouped by AddOns folder
/// (the NA and EU servers have separate folders).
/// </summary>
public sealed class JsonInstallRecordStore : IInstallRecordStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly Lock _lock = new();
    private readonly string _filePath;
    private Dictionary<string, Dictionary<string, InstallRecord>>? _data;

    public JsonInstallRecordStore() : this(Path.Combine(AppPaths.RoamingFolder, "installed.json"))
    {
    }

    internal JsonInstallRecordStore(string filePath)
    {
        _filePath = filePath;
    }

    public IReadOnlyDictionary<string, InstallRecord> GetAll(string addonsFolder)
    {
        lock (_lock)
        {
            var data = Load();
            return data.TryGetValue(Key(addonsFolder), out var records)
                ? new Dictionary<string, InstallRecord>(records, StringComparer.Ordinal)
                : new Dictionary<string, InstallRecord>(StringComparer.Ordinal);
        }
    }

    public void Save(string addonsFolder, InstallRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        lock (_lock)
        {
            var data = Load();
            var key = Key(addonsFolder);
            if (!data.TryGetValue(key, out var records))
                data[key] = records = new Dictionary<string, InstallRecord>(StringComparer.Ordinal);

            // Folders now owned by this addon no longer belong to the addons that installed them before.
            foreach (var (id, other) in records.ToList())
            {
                if (id == record.AddonId)
                    continue;

                var remaining = other.Directories
                    .Where(d => !record.Directories.Contains(d, StringComparer.OrdinalIgnoreCase))
                    .ToList();
                if (remaining.Count == 0)
                    records.Remove(id);
                else if (remaining.Count != other.Directories.Count)
                    records[id] = other with { Directories = remaining };
            }

            records[record.AddonId] = record;

            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var temp = _filePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(data, SerializerOptions));
            File.Move(temp, _filePath, overwrite: true);
        }
    }

    private Dictionary<string, Dictionary<string, InstallRecord>> Load()
    {
        if (_data is not null)
            return _data;

        try
        {
            if (File.Exists(_filePath))
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, InstallRecord>>>(
                    File.ReadAllText(_filePath), SerializerOptions);
                if (loaded is not null)
                {
                    return _data = loaded.ToDictionary(
                        p => p.Key,
                        p => new Dictionary<string, InstallRecord>(p.Value, StringComparer.Ordinal),
                        StringComparer.OrdinalIgnoreCase);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A broken file only costs the exact version tracking; manifests are used instead.
        }

        return _data = new Dictionary<string, Dictionary<string, InstallRecord>>(StringComparer.OrdinalIgnoreCase);
    }

    private static string Key(string addonsFolder) =>
        Path.GetFullPath(addonsFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
