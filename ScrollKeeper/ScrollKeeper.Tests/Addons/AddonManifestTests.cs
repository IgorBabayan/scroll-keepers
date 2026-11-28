using ScrollKeeper.Services.Addons;
using ScrollKeeper.Tests.Support;

namespace ScrollKeeper.Tests.Addons;

public sealed class AddonManifestTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Parse_ReadsHeadersAndStripsFormatting()
    {
        var manifest = AddonManifest.Parse(
        [
            "## Title: |cFF0000My|r   Addon",
            "## Author: |t32:32:icon.dds|tSomeone",
            "## Version: 1.2.3",
            "## AddOnVersion: 123",
            "## IsLibrary: TRUE",
            "## Description: Does things"
        ], "Fallback");

        Assert.Equal("My Addon", manifest.Title);
        Assert.Equal("Someone", manifest.Author);
        Assert.Equal("1.2.3", manifest.Version);
        Assert.Equal(123, manifest.AddOnVersion);
        Assert.True(manifest.IsLibrary);
        Assert.Equal("Does things", manifest.Description);
    }

    [Theory]
    [InlineData("")]
    [InlineData("## Title:   ")]
    [InlineData("## Title: |cFFFFFF|r")]
    public void Parse_UsesFallbackTitle_WhenTitleIsMissingOrEmpty(string header)
    {
        string[] lines = header.Length == 0 ? [] : [header];

        var manifest = AddonManifest.Parse(lines, "FolderName");

        Assert.Equal("FolderName", manifest.Title);
    }

    [Fact]
    public void Parse_FirstHeaderOccurrenceWins_AndKeysAreCaseInsensitive()
    {
        var manifest = AddonManifest.Parse(["## Version: 1", "## version: 2"], "X");

        Assert.Equal("1", manifest.Version);
    }

    [Fact]
    public void Parse_IgnoresCommentsLinesWithoutColonAndFileLines()
    {
        var manifest = AddonManifest.Parse(
        [
            "; a comment",
            "MyAddon.lua",
            "## NoColonHere",
            "  ## Version: 2.0  "
        ], "X");

        Assert.Equal("2.0", manifest.Version);
        Assert.Equal("X", manifest.Title);
    }

    [Fact]
    public void Parse_InvalidAddOnVersion_IsZero_AndIsLibraryDefaultsToFalse()
    {
        var manifest = AddonManifest.Parse(["## AddOnVersion: abc", "## IsLibrary: yes"], "X");

        Assert.Equal(0, manifest.AddOnVersion);
        Assert.False(manifest.IsLibrary);
    }

    [Fact]
    public void Parse_MergesDependsOnAndPcDependsOn_AndSkipsConsoleLines()
    {
        var manifest = AddonManifest.Parse(
        [
            "## DependsOn: LibA>=2 LibB",
            "## PCDependsOn: LibA>=5 LibC",
            "## ConsoleDependsOn: LibConsole",
            "## DependsOn: LibD"
        ], "X");

        Assert.Equal(
            [new AddonDependency("LibA", 5), new AddonDependency("LibB", null), new AddonDependency("LibC", null), new AddonDependency("LibD", null)],
            manifest.DependsOn);
        Assert.Empty(manifest.OptionalDependsOn);
    }

    [Fact]
    public void Parse_ReadsOptionalDependenciesSeparately()
    {
        var manifest = AddonManifest.Parse(
        [
            "## OptionalDependsOn: LibOpt>=3",
            "## PCOptionalDependsOn: LibPc",
            "## ConsoleOptionalDependsOn: LibConsole"
        ], "X");

        Assert.Empty(manifest.DependsOn);
        Assert.Equal([new AddonDependency("LibOpt", 3), new AddonDependency("LibPc", null)], manifest.OptionalDependsOn);
    }

    [Fact]
    public void Parse_SplitsAndDeduplicatesSavedVariables_AndSkipsConsoleOnes()
    {
        var manifest = AddonManifest.Parse(
        [
            "## SavedVariables: First Second,Third\tFirst",
            "## PCSavedVariables: Fourth",
            "## ConsoleSavedVariables: ConsoleOnly"
        ], "X");

        Assert.Equal(["First", "Second", "Third", "Fourth"], manifest.SavedVariables);
    }

    [Fact]
    public void Parse_StopsReadingAfterMaxLines()
    {
        var lines = Enumerable.Repeat("filler.lua", 400).Append("## Title: Too late");

        var manifest = AddonManifest.Parse(lines, "Fallback");

        Assert.Equal("Fallback", manifest.Title);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("Plain", "Plain")]
    [InlineData("|cffffffWhite|r text", "White text")]
    [InlineData("|t64:64:EsoUI/Art/icon.dds|t Icon", "Icon")]
    [InlineData("  many \t  spaces  ", "many spaces")]
    [InlineData("|cGGGGGGnot a color", "|cGGGGGGnot a color")]
    public void StripFormatting_RemovesEsoMarkup(string? input, string expected)
    {
        Assert.Equal(expected, AddonManifest.StripFormatting(input));
    }

    [Fact]
    public void TryRead_ReadsTxtManifest_NamedAfterTheFolder()
    {
        var folder = _temp.WriteManifest("MyAddon", "## Title: My Addon", "## Version: 1.0");

        var manifest = AddonManifest.TryRead(folder);

        Assert.NotNull(manifest);
        Assert.Equal("My Addon", manifest.Title);
    }

    [Fact]
    public void TryRead_PrefersAddonFileOverTxt()
    {
        var folder = _temp.WriteManifestFile("MyAddon", ".txt", "## Version: txt");
        _temp.WriteManifestFile("MyAddon", ".addon", "## Version: addon");

        var manifest = AddonManifest.TryRead(folder);

        Assert.NotNull(manifest);
        Assert.Equal("addon", manifest.Version);
    }

    [Fact]
    public void TryRead_HandlesTrailingSeparator_AndUsesFolderNameAsFallbackTitle()
    {
        var folder = _temp.WriteManifest("MyAddon", "## Version: 1");

        var manifest = AddonManifest.TryRead(folder + Path.DirectorySeparatorChar);

        Assert.NotNull(manifest);
        Assert.Equal("MyAddon", manifest.Title);
    }

    [Fact]
    public void TryRead_ReturnsNull_WhenFolderHasNoManifest()
    {
        var folder = Path.Combine(_temp.Path, "NotAnAddon");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Other.txt"), "## Title: Other");

        Assert.Null(AddonManifest.TryRead(folder));
    }
}
