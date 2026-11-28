using ScrollKeeper.Services.Addons;

namespace ScrollKeeper.Tests.Addons;

public sealed class AddonDependencyTests
{
    [Theory]
    [InlineData("LibAddonMenu-2.0>=32", "LibAddonMenu-2.0", 32)]
    [InlineData("LibStub", "LibStub", null)]
    [InlineData("  LibA >= 7 ", "LibA", 7)]
    [InlineData("LibA>=abc", "LibA", null)]
    [InlineData("LibA>=", "LibA", null)]
    public void TryParse_ReadsNameAndMinimumVersion(string token, string name, int? minVersion)
    {
        Assert.Equal(new AddonDependency(name, minVersion), AddonDependency.TryParse(token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(">=3")]
    public void TryParse_ReturnsNull_ForEmptyName(string token)
    {
        Assert.Null(AddonDependency.TryParse(token));
    }

    [Fact]
    public void ParseList_KeepsHighestMinimumVersion_ForRepeatedNames()
    {
        var target = new List<AddonDependency>();

        AddonDependency.ParseList("LibA>=1 LibA>=3 liba>=2", target);

        Assert.Equal([new AddonDependency("LibA", 3)], target);
    }

    [Fact]
    public void ParseList_VersionedEntryReplacesUnversioned_ButNotTheOtherWayAround()
    {
        var first = new List<AddonDependency>();
        AddonDependency.ParseList("LibA LibA>=2", first);

        var second = new List<AddonDependency>();
        AddonDependency.ParseList("LibA>=2 LibA", second);

        Assert.Equal([new AddonDependency("LibA", 2)], first);
        Assert.Equal([new AddonDependency("LibA", 2)], second);
    }

    [Fact]
    public void ParseList_AppendsToExistingEntries_AndStripsFormatting()
    {
        var target = new List<AddonDependency> { new("LibA", 1) };

        AddonDependency.ParseList("|cFF0000LibB|r   LibA>=4", target);

        Assert.Equal([new AddonDependency("LibA", 4), new AddonDependency("LibB", null)], target);
    }

    [Theory]
    [InlineData("LibA", null, "LibA")]
    [InlineData("LibA", 12, "LibA>=12")]
    public void ToString_UsesManifestSyntax(string name, int? minVersion, string expected)
    {
        Assert.Equal(expected, new AddonDependency(name, minVersion).ToString());
    }
}
