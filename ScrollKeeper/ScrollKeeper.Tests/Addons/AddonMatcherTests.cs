using ScrollKeeper.Services.Addons;
using ScrollKeeper.Tests.Support;

namespace ScrollKeeper.Tests.Addons;

public sealed class AddonMatcherTests
{
    private static readonly Dictionary<string, InstallRecord> NoRecords = [];

    [Fact]
    public void Match_GroupsFoldersOfOneCatalogEntry_WithMainFolderFirst()
    {
        var addon = TestData.Catalog("1", "1.0", 0, "Main", "Extra");

        var result = AddonMatcher.Match([TestData.Local("Extra"), TestData.Local("main", "1.0")], [addon], NoRecords);

        var installed = Assert.Single(result);
        Assert.Equal("1", installed.Key);
        Assert.Same(addon, installed.Catalog);
        Assert.Equal(["main", "Extra"], installed.Folders.Select(f => f.FolderName));
        Assert.Equal("main", installed.MainFolder.FolderName);
    }

    [Fact]
    public void Match_UnknownFolder_GetsLocalKey_AndNeverHasUpdate()
    {
        var result = AddonMatcher.Match([TestData.Local("Unknown", "1.0")], [], NoRecords);

        var installed = Assert.Single(result);
        Assert.Equal(AddonMatcher.LocalKeyPrefix + "Unknown", installed.Key);
        Assert.Null(installed.Catalog);
        Assert.False(installed.HasUpdate);
        Assert.Equal("1.0", installed.InstalledVersion);
    }

    [Fact]
    public void Match_KeepsOrderOfFirstAppearance()
    {
        var a = TestData.Catalog("A", "1", 0, "FolderA");
        var b = TestData.Catalog("B", "1", 0, "FolderB");

        var result = AddonMatcher.Match([TestData.Local("FolderB"), TestData.Local("Loose"), TestData.Local("FolderA")], [a, b], NoRecords);

        Assert.Equal(["B", "local:Loose", "A"], result.Select(r => r.Key));
    }

    [Fact]
    public void Match_PrefersEntryWhoseMainFolderItIs_ForSharedLibraries()
    {
        var bundle = TestData.Catalog("Bundle", "1", 1_000_000, "Big", "LibShared");
        var library = TestData.Catalog("Lib", "1", 1, "LibShared");

        var result = AddonMatcher.Match([TestData.Local("LibShared")], [bundle, library], NoRecords);

        Assert.Equal("Lib", Assert.Single(result).Key);
    }

    [Fact]
    public void Match_RecordedOwnerWins_OverCatalogLookup()
    {
        var bundle = TestData.Catalog("Bundle", "2", 0, "Big", "LibShared");
        var library = TestData.Catalog("Lib", "1", 0, "LibShared");
        var records = new Dictionary<string, InstallRecord>
        {
            ["Bundle"] = TestData.Record("Bundle", "2", "Big", "LibShared")
        };

        var result = AddonMatcher.Match([TestData.Local("Big"), TestData.Local("LibShared")], [bundle, library], records);

        var installed = Assert.Single(result);
        Assert.Equal("Bundle", installed.Key);
        Assert.Equal(2, installed.Folders.Count);
    }

    [Fact]
    public void Match_IgnoresRecordsOfAddonsNoLongerInCatalog()
    {
        var library = TestData.Catalog("Lib", "1", 0, "LibShared");
        var records = new Dictionary<string, InstallRecord>
        {
            ["Gone"] = TestData.Record("Gone", "1", "LibShared")
        };

        var result = AddonMatcher.Match([TestData.Local("LibShared")], [library], records);

        Assert.Equal("Lib", Assert.Single(result).Key);
    }

    [Fact]
    public void Match_UsesRecordedVersion_AndAnyDifferenceIsAnUpdate()
    {
        var addon = TestData.Catalog("1", "r2", 0, "Main");
        var records = new Dictionary<string, InstallRecord> { ["1"] = TestData.Record("1", "r1", "Main") };

        var installed = Assert.Single(AddonMatcher.Match([TestData.Local("Main", "9.9")], [addon], records));

        Assert.Equal("r1", installed.InstalledVersion);
        Assert.True(installed.HasUpdate);
    }

    [Fact]
    public void Match_FallsBackToManifestVersion_WithoutRecord()
    {
        var addon = TestData.Catalog("1", "1.5", 0, "Main");

        var installed = Assert.Single(AddonMatcher.Match([TestData.Local("Main", "1.5")], [addon], NoRecords));

        Assert.Equal("1.5", installed.InstalledVersion);
        Assert.False(installed.HasUpdate);
    }

    [Theory]
    [InlineData("1.2", "1.2", false, false)]
    [InlineData("v1.3", "1.2", false, true)]
    [InlineData("1.2", "1.3", false, false)]
    [InlineData("1.2.10", "1.2.9", false, true)]
    [InlineData("1.2.0", "1.2", false, false)]
    [InlineData("V2", "v2", false, false)]
    [InlineData("|cFF00001.2|r", "1.2", false, false)]
    [InlineData("release", "beta", false, true)]
    [InlineData("", "1.0", false, false)]
    [InlineData("1.0", "", false, false)]
    [InlineData("1.0", "1.1", true, true)]
    [InlineData("1.0", "1.0", true, false)]
    public void IsUpdateAvailable_ComparesVersions(string latest, string installed, bool isRecorded, bool expected)
    {
        Assert.Equal(expected, AddonMatcher.IsUpdateAvailable(latest, installed, isRecorded));
    }

    [Theory]
    [InlineData("v1.2.10", "1.2.9", 1)]
    [InlineData("1.0", "1", 0)]
    [InlineData("2", "10", -1)]
    [InlineData("Build 3.1 beta", "3.0.9", 1)]
    public void CompareNumbers_ComparesFirstDottedNumber(string left, string right, int expected)
    {
        Assert.Equal(expected, AddonMatcher.CompareNumbers(left, right));
    }

    [Theory]
    [InlineData("abc", "1.0")]
    [InlineData("1.0", "abc")]
    public void CompareNumbers_ReturnsNull_WhenAVersionHasNoNumber(string left, string right)
    {
        Assert.Null(AddonMatcher.CompareNumbers(left, right));
    }
}
