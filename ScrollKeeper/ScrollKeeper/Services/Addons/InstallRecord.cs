namespace ScrollKeeper.Services.Addons;

/// <summary>What ScrollKeeper installed for one ESOUI addon.</summary>
/// <param name="AddonId">ESOUI file id.</param>
/// <param name="Version">ESOUI version string of the installed upload.</param>
/// <param name="Directories">Top-level AddOns folders the archive contained.</param>
/// <param name="InstalledAt">When it was installed.</param>
public sealed record InstallRecord(
    string AddonId,
    string Version,
    IReadOnlyList<string> Directories,
    DateTimeOffset InstalledAt);
