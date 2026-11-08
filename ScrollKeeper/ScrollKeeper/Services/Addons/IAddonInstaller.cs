using ScrollKeeper.Services.Catalog;

namespace ScrollKeeper.Services.Addons;

public enum InstallStage
{
    Downloading,
    Installing
}

/// <summary>Progress of one install.</summary>
/// <param name="Stage">Download first, then extraction into AddOns.</param>
/// <param name="Fraction">Completed fraction of the current stage (0..1), or <c>null</c> if unknown.</param>
/// <param name="BytesReceived">Downloaded bytes (download stage).</param>
/// <param name="TotalBytes">Archive size in bytes, or 0 if unknown.</param>
public readonly record struct InstallProgress(InstallStage Stage, double? Fraction, long BytesReceived = 0, long TotalBytes = 0);

/// <summary>Downloads an addon from ESOUI and installs (or updates) it in the AddOns folder.</summary>
public interface IAddonInstaller
{
    /// <summary>
    /// Downloads the current upload of <paramref name="addon"/>, replaces its folders in
    /// <paramref name="addonsFolder"/> and records the installed version.
    /// </summary>
    /// <param name="asDependency">
    /// Installed because another addon requires it. An update keeps the reason of the first install.
    /// </param>
    Task<InstallRecord> InstallAsync(
        CatalogAddon addon,
        string addonsFolder,
        IProgress<InstallProgress>? progress,
        bool asDependency = false,
        CancellationToken cancellationToken = default);
}
