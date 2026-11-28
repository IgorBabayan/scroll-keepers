using ScrollKeeper.Services.Addons;
using ScrollKeeper.Services.Catalog;

namespace ScrollKeeper.Tests.Support;

internal static class TestData
{
    public static CatalogAddon Catalog(string id, string version = "1.0", long downloads = 0, params string[] directories) =>
        new(
            Id: id,
            CategoryId: "1",
            Name: "Addon " + id,
            Author: "Author",
            Version: version,
            Updated: null,
            InfoUrl: null,
            Downloads: downloads,
            MonthlyDownloads: 0,
            Favorites: 0,
            Directories: directories,
            Thumbnails: [],
            Images: [],
            Compatibility: []);

    public static AddonManifest Manifest(
        string title = "Title",
        string version = "",
        int addOnVersion = 0,
        IReadOnlyList<AddonDependency>? dependsOn = null,
        IReadOnlyList<AddonDependency>? optionalDependsOn = null) =>
        new(title, "Author", version, addOnVersion, IsLibrary: false, "", dependsOn ?? [], optionalDependsOn ?? [], []);

    public static LocalAddon Local(string folder, string version = "") =>
        new(folder, "/AddOns/" + folder, Manifest(folder, version));

    public static InstallRecord Record(string addonId, string version, params string[] directories) =>
        new(addonId, version, directories, DateTimeOffset.UnixEpoch);
}
