namespace ScrollKeeper.Services.Addons;

/// <summary>
/// Remembers the ESOUI version installed for each addon, so updates are detected from the exact
/// version string ESOUI publishes rather than from the manifest (which authors do not always update).
/// </summary>
public interface IInstallRecordStore
{
    /// <summary>Records of one AddOns folder, by addon id.</summary>
    IReadOnlyDictionary<string, InstallRecord> GetAll(string addonsFolder);

    void Save(string addonsFolder, InstallRecord record);
}
