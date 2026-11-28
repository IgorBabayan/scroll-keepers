using ScrollKeeper.Services.Addons;
using ScrollKeeper.Tests.Support;

namespace ScrollKeeper.Tests.Addons;

public sealed class LocalAddonScannerTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Scan_ReturnsNothing_ForBlankFolder(string folder)
    {
        Assert.Empty(new LocalAddonScanner().Scan(folder));
    }

    [Fact]
    public void Scan_ReturnsNothing_ForMissingFolder()
    {
        Assert.Empty(new LocalAddonScanner().Scan(Path.Combine(_temp.Path, "Missing")));
    }

    [Fact]
    public void Scan_ReturnsTopLevelAddons_SortedByTitle()
    {
        _temp.WriteManifest("Zeta", "## Title: alpha");
        _temp.WriteManifest("Alpha", "## Title: Beta");
        _temp.WriteManifest(Path.Combine("Alpha", "Nested"), "## Title: Nested");

        var result = new LocalAddonScanner().Scan(_temp.Path);

        Assert.Equal(["Zeta", "Alpha"], result.Select(a => a.FolderName));
        Assert.Equal(Path.Combine(_temp.Path, "Zeta"), result[0].Path);
        Assert.Equal("alpha", result[0].Manifest.Title);
    }

    [Fact]
    public void Scan_SkipsHiddenFolders_AndFoldersWithoutManifest()
    {
        _temp.WriteManifest(".staging", "## Title: Staging");
        Directory.CreateDirectory(Path.Combine(_temp.Path, "Empty"));
        _temp.WriteManifest("Real", "## Title: Real");

        var result = new LocalAddonScanner().Scan(_temp.Path);

        Assert.Equal("Real", Assert.Single(result).FolderName);
    }
}
