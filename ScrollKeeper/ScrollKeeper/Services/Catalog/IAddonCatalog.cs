namespace ScrollKeeper.Services.Catalog;

/// <summary>The online addon catalog (ESOUI, the same source Minion uses).</summary>
public interface IAddonCatalog
{
    /// <summary>
    /// Downloads the addon and category lists. If ESOUI cannot be reached, returns the last saved copy
    /// (<see cref="AddonCatalogSnapshot.IsFromCache"/>), or throws when there is none.
    /// </summary>
    Task<AddonCatalogSnapshot> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Downloads the full information of one addon (description, change log, download URL).</summary>
    Task<AddonDetails> GetDetailsAsync(string addonId, CancellationToken cancellationToken = default);
}
