namespace ScrollKeeper.Services.Catalog;

/// <summary>An addon published on ESOUI (one entry of <c>filelist.json</c>).</summary>
/// <param name="Id">ESOUI file id (UID), e.g. <c>"7"</c>.</param>
/// <param name="CategoryId">ESOUI category id.</param>
/// <param name="Name">Display name.</param>
/// <param name="Author">Author name.</param>
/// <param name="Version">Version string set by the author (free text).</param>
/// <param name="Updated">Time of the last upload.</param>
/// <param name="InfoUrl">Web page of the addon on esoui.com.</param>
/// <param name="Downloads">Total downloads.</param>
/// <param name="MonthlyDownloads">Downloads during the last month.</param>
/// <param name="Favorites">How many users marked the addon as favorite.</param>
/// <param name="Directories">Top-level folders the addon's archive installs into AddOns.</param>
/// <param name="Thumbnails">Screenshot thumbnail URLs.</param>
/// <param name="Images">Screenshot URLs (same order as <paramref name="Thumbnails"/>).</param>
/// <param name="Compatibility">Game versions the author marked the addon compatible with.</param>
public sealed record CatalogAddon(
    string Id,
    string CategoryId,
    string Name,
    string Author,
    string Version,
    DateTimeOffset? Updated,
    string? InfoUrl,
    long Downloads,
    long MonthlyDownloads,
    long Favorites,
    IReadOnlyList<string> Directories,
    IReadOnlyList<string> Thumbnails,
    IReadOnlyList<string> Images,
    IReadOnlyList<GameCompatibility> Compatibility);

/// <summary>A game version, e.g. <c>11.2.0</c> "Seasons of the Worm Cult".</summary>
public sealed record GameCompatibility(string Version, string Name);

/// <summary>An ESOUI addon category (one entry of <c>categorylist.json</c>).</summary>
public sealed record CatalogCategory(string Id, string Title, string? IconUrl, IReadOnlyList<string> ParentIds);

/// <summary>Everything the ESOUI page of an addon shows (<c>filedetails/{id}.json</c>).</summary>
/// <param name="Id">ESOUI file id.</param>
/// <param name="Version">Version string of the current upload.</param>
/// <param name="FileName">File name of the archive.</param>
/// <param name="DownloadUrl">Direct download URL of the archive (.zip).</param>
/// <param name="Description">Description in ESOUI BBCode.</param>
/// <param name="ChangeLog">Change log in ESOUI BBCode.</param>
/// <param name="Views">Page views.</param>
/// <param name="MonthlyViews">Page views during the last month.</param>
/// <param name="IsPending">An update waits for approval by the ESOUI staff.</param>
public sealed record AddonDetails(
    string Id,
    string Version,
    string? FileName,
    string? DownloadUrl,
    string Description,
    string ChangeLog,
    long Views,
    long MonthlyViews,
    bool IsPending);

/// <summary>The addon list together with its categories.</summary>
/// <param name="Addons">All published addons.</param>
/// <param name="Categories">Categories by id.</param>
/// <param name="IsFromCache">The list comes from the last saved copy because ESOUI could not be reached.</param>
public sealed record AddonCatalogSnapshot(
    IReadOnlyList<CatalogAddon> Addons,
    IReadOnlyDictionary<string, CatalogCategory> Categories,
    bool IsFromCache);
