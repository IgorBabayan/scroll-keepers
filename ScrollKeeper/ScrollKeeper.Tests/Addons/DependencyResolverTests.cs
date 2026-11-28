using ScrollKeeper.Services.Addons;
using ScrollKeeper.Services.Catalog;
using ScrollKeeper.Tests.Support;

namespace ScrollKeeper.Tests.Addons;

public sealed class DependencyResolverTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static (string, AddonManifest) Requires(string folder, params AddonDependency[] dependsOn) =>
        (folder, TestData.Manifest(folder, dependsOn: dependsOn));

    private static AddonDependency D(string name, int? minVersion) => new(name, minVersion);

    private static Dictionary<string, int> Available(params (string Name, int Version)[] items) =>
        items.ToDictionary(i => i.Name, i => i.Version, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Resolve_ReturnsNothing_WhenEveryDependencyIsInstalled()
    {
        var result = DependencyResolver.Resolve(
            [Requires("MyAddon", D("LibA", 2), D("LibB", null))],
            Available(("LibA", 2), ("libb", 0)),
            []);

        Assert.Empty(result);
    }

    [Fact]
    public void Resolve_ReportsMissingDependency_WithItsCatalogSource()
    {
        var lib = TestData.Catalog("10", "1.0", 5, "LibA");

        var result = DependencyResolver.Resolve([Requires("MyAddon", D("LibA", null))], Available(), [lib]);

        var missing = Assert.Single(result);
        Assert.Equal(new AddonDependency("LibA", null), missing.Dependency);
        Assert.Equal("MyAddon", missing.RequiredBy);
        Assert.Null(missing.InstalledAddOnVersion);
        Assert.Same(lib, missing.Source);
    }

    [Fact]
    public void Resolve_ReportsTooOldDependency_WithInstalledVersion()
    {
        var result = DependencyResolver.Resolve([Requires("MyAddon", D("LibA", 5))], Available(("LibA", 3)), []);

        var missing = Assert.Single(result);
        Assert.Equal(3, missing.InstalledAddOnVersion);
        Assert.Null(missing.Source);
    }

    [Fact]
    public void Resolve_ReportsEachDependencyOnce_ForTheFirstAddonThatNeedsIt()
    {
        var result = DependencyResolver.Resolve(
            [Requires("First", D("LibA", null)), Requires("Second", D("liba", null), D("LibB", null))],
            Available(),
            []);

        Assert.Equal(["LibA", "LibB"], result.Select(m => m.Dependency.Name));
        Assert.Equal(["First", "Second"], result.Select(m => m.RequiredBy));
    }

    [Fact]
    public void Resolve_IgnoresOptionalDependencies()
    {
        var manifest = TestData.Manifest("MyAddon", optionalDependsOn: [new AddonDependency("LibOpt", null)]);

        Assert.Empty(DependencyResolver.Resolve([("MyAddon", manifest)], Available(), []));
    }

    [Fact]
    public void Resolve_PrefersCatalogEntryWhoseMainFolderIsTheDependency()
    {
        var bundle = TestData.Catalog("1", "1.0", 1_000_000, "BigAddon", "LibA");
        var own = TestData.Catalog("2", "1.0", 10, "LibA");

        var result = DependencyResolver.Resolve([Requires("MyAddon", D("LibA", null))], Available(), [bundle, own]);

        Assert.Same(own, Assert.Single(result).Source);
    }

    [Fact]
    public void Resolve_ThenPrefersFewestFolders_ThenMostDownloads()
    {
        var large = TestData.Catalog("1", "1.0", 1_000, "Big", "Other", "LibA");
        var smallFew = TestData.Catalog("2", "1.0", 10, "Small", "LibA");
        var smallMany = TestData.Catalog("3", "1.0", 500, "Small2", "LibA");

        var result = DependencyResolver.Resolve([Requires("MyAddon", D("LibA", null))], Available(), [large, smallFew, smallMany]);

        Assert.Same(smallMany, Assert.Single(result).Source);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FindMissing_ReturnsNothing_ForBlankFolder(string folder)
    {
        Assert.Empty(new DependencyResolver().FindMissing(folder, ["MyAddon"], []));
    }

    [Fact]
    public void FindMissing_ReturnsNothing_ForMissingFolder()
    {
        var folder = Path.Combine(_temp.Path, "DoesNotExist");

        Assert.Empty(new DependencyResolver().FindMissing(folder, ["MyAddon"], []));
    }

    [Fact]
    public void FindMissing_CountsBundledLibraries_AndReportsTooOldOnes()
    {
        _temp.WriteManifest("MyAddon", "## DependsOn: LibA>=2 LibBundled LibMissing");
        _temp.WriteManifest(Path.Combine("MyAddon", "libs", "LibBundled"), "## AddOnVersion: 1");
        _temp.WriteManifest("LibA", "## AddOnVersion: 1");
        var catalog = new[] { TestData.Catalog("7", "2.0", 0, "LibMissing") };

        var result = new DependencyResolver().FindMissing(_temp.Path, ["MyAddon"], catalog);

        Assert.Equal(["LibA", "LibMissing"], result.Select(m => m.Dependency.Name));
        Assert.Equal(1, result[0].InstalledAddOnVersion);
        Assert.Same(catalog[0], result[1].Source);
    }

    [Fact]
    public void FindMissing_ReadsDependenciesOfNestedLibraries()
    {
        _temp.WriteManifest("MyAddon", "## Title: My Addon");
        _temp.WriteManifest(Path.Combine("MyAddon", "LibInner"), "## DependsOn: LibDeep");

        var result = new DependencyResolver().FindMissing(_temp.Path, ["MyAddon"], []);

        var missing = Assert.Single(result);
        Assert.Equal("LibDeep", missing.Dependency.Name);
        Assert.Equal("LibInner", missing.RequiredBy);
    }

    [Fact]
    public void FindMissing_SkipsDirectoriesThatAreNotInstalled()
    {
        Assert.Empty(new DependencyResolver().FindMissing(_temp.Path, ["NotThere"], []));
    }

    [Fact]
    public void ScanAvailable_KeepsHighestVersionOfDuplicates()
    {
        _temp.WriteManifest("LibA", "## AddOnVersion: 3");
        _temp.WriteManifest(Path.Combine("Other", "LibA"), "## AddOnVersion: 9");
        _temp.WriteManifest(Path.Combine("Third", "LibA"), "## AddOnVersion: 4");

        var available = DependencyResolver.ScanAvailable(_temp.Path);

        Assert.Equal(9, available["liba"]);
    }

    [Fact]
    public void ScanAvailable_SkipsHiddenFolders_AndFoldersDeeperThanTheGameLoads()
    {
        _temp.WriteManifest(Path.Combine(".backup", "LibHidden"), "## AddOnVersion: 1");
        _temp.WriteManifest(Path.Combine("A", "B", "LibDepth3"), "## AddOnVersion: 1");
        _temp.WriteManifest(Path.Combine("A", "B", "C", "LibDepth4"), "## AddOnVersion: 1");

        var available = DependencyResolver.ScanAvailable(_temp.Path);

        Assert.True(available.ContainsKey("LibDepth3"));
        Assert.False(available.ContainsKey("LibDepth4"));
        Assert.False(available.ContainsKey("LibHidden"));
    }
}
