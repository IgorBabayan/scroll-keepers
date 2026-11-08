using ScrollKeeper.Services.Catalog;

namespace ScrollKeeper.Services.Addons;

/// <summary>A required dependency that is missing from AddOns (or too old) and how to get it.</summary>
/// <param name="Dependency">The <c>## DependsOn</c> entry.</param>
/// <param name="RequiredBy">Folder name of the addon that requires it.</param>
/// <param name="InstalledAddOnVersion">The <c>## AddOnVersion</c> found in AddOns when it is too old, otherwise <c>null</c>.</param>
/// <param name="Source">The ESOUI entry that provides it, or <c>null</c> when ESOUI has none.</param>
public sealed record MissingDependency(
    AddonDependency Dependency,
    string RequiredBy,
    int? InstalledAddOnVersion,
    CatalogAddon? Source);

/// <summary>Finds the libraries and addons an installed addon needs but AddOns does not have.</summary>
public interface IDependencyResolver
{
    /// <summary>
    /// Reads the <c>## DependsOn</c> headers of <paramref name="directories"/> (and of the folders nested in them)
    /// and returns the required ones that no folder in <paramref name="addonsFolder"/> satisfies.
    /// Optional dependencies are ignored.
    /// </summary>
    IReadOnlyList<MissingDependency> FindMissing(
        string addonsFolder,
        IReadOnlyList<string> directories,
        IReadOnlyList<CatalogAddon> catalog);
}
