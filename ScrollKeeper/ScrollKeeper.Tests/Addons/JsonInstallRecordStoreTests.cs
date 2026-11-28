using ScrollKeeper.Services.Addons;
using ScrollKeeper.Tests.Support;

namespace ScrollKeeper.Tests.Addons;

public sealed class JsonInstallRecordStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _file;
    private readonly string _addons;

    public JsonInstallRecordStoreTests()
    {
        _file = Path.Combine(_temp.Path, "data", "installed.json");
        _addons = Path.Combine(_temp.Path, "AddOns");
    }

    public void Dispose() => _temp.Dispose();

    private JsonInstallRecordStore CreateStore() => new(_file);

    [Fact]
    public void GetAll_ReturnsEmpty_WhenNothingWasSaved()
    {
        Assert.Empty(CreateStore().GetAll(_addons));
        Assert.False(File.Exists(_file));
    }

    [Fact]
    public void Save_PersistsRecords_ForANewStoreInstance()
    {
        var record = new InstallRecord("1", "2.0", ["Main", "Extra"], DateTimeOffset.UnixEpoch, IsDependency: true);
        CreateStore().Save(_addons, record);

        var loaded = Assert.Single(CreateStore().GetAll(_addons)).Value;

        Assert.Equal("1", loaded.AddonId);
        Assert.Equal("2.0", loaded.Version);
        Assert.Equal(["Main", "Extra"], loaded.Directories);
        Assert.Equal(DateTimeOffset.UnixEpoch, loaded.InstalledAt);
        Assert.True(loaded.IsDependency);
    }

    [Fact]
    public void Save_KeepsAddOnsFoldersSeparate_AndNormalizesTheirPaths()
    {
        var store = CreateStore();
        var other = Path.Combine(_temp.Path, "OtherAddOns");
        store.Save(_addons, TestData.Record("1", "1", "Main"));
        store.Save(other, TestData.Record("2", "1", "Main"));

        Assert.Equal(["1"], store.GetAll(_addons + Path.DirectorySeparatorChar).Keys);
        Assert.Equal(["2"], store.GetAll(other).Keys);
    }

    [Fact]
    public void Save_MovesFolderOwnership_ToTheNewRecord()
    {
        var store = CreateStore();
        store.Save(_addons, TestData.Record("A", "1", "AddonA", "LibShared"));
        store.Save(_addons, TestData.Record("B", "1", "AddonB", "libshared"));

        var records = store.GetAll(_addons);

        Assert.Equal(["AddonA"], records["A"].Directories);
        Assert.Equal(["AddonB", "libshared"], records["B"].Directories);
    }

    [Fact]
    public void Save_DeletesRecordsLeftWithoutFolders()
    {
        var store = CreateStore();
        store.Save(_addons, TestData.Record("A", "1", "Shared"));
        store.Save(_addons, TestData.Record("B", "1", "Shared"));

        Assert.Equal(["B"], store.GetAll(_addons).Keys);
    }

    [Fact]
    public void Save_ReplacesTheRecordOfTheSameAddon()
    {
        var store = CreateStore();
        store.Save(_addons, TestData.Record("A", "1", "Main"));
        store.Save(_addons, TestData.Record("A", "2", "Main"));

        Assert.Equal("2", Assert.Single(store.GetAll(_addons)).Value.Version);
    }

    [Fact]
    public void RemoveDirectories_TrimsRecords_AndDeletesEmptyOnes()
    {
        var store = CreateStore();
        store.Save(_addons, TestData.Record("A", "1", "AddonA", "Extra"));
        store.Save(_addons, TestData.Record("B", "1", "AddonB"));

        store.RemoveDirectories(_addons, ["extra", "AddonB"]);

        var records = CreateStore().GetAll(_addons);
        Assert.Equal(["A"], records.Keys);
        Assert.Equal(["AddonA"], records["A"].Directories);
    }

    [Fact]
    public void RemoveDirectories_AlwaysDeletesTheRemovedAddon()
    {
        var store = CreateStore();
        store.Save(_addons, TestData.Record("A", "1", "AddonA"));

        store.RemoveDirectories(_addons, [], removedAddonId: "A");

        Assert.Empty(CreateStore().GetAll(_addons));
    }

    [Fact]
    public void RemoveDirectories_DoesNothing_ForUnknownFolder()
    {
        CreateStore().RemoveDirectories(_addons, ["Anything"], removedAddonId: "A");

        Assert.False(File.Exists(_file));
    }

    [Fact]
    public void GetAll_ReturnsACopy()
    {
        var store = CreateStore();
        store.Save(_addons, TestData.Record("A", "1", "AddonA"));
        var before = store.GetAll(_addons);

        store.Save(_addons, TestData.Record("B", "1", "AddonB"));

        Assert.Single(before);
    }

    [Fact]
    public void GetAll_ReturnsEmpty_ForCorruptedFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllText(_file, "{ not json");

        Assert.Empty(CreateStore().GetAll(_addons));
    }

    [Fact]
    public void Save_OverwritesCorruptedFile_WithoutLeavingTempFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllText(_file, "garbage");

        CreateStore().Save(_addons, TestData.Record("A", "1", "AddonA"));

        Assert.Single(CreateStore().GetAll(_addons));
        Assert.False(File.Exists(_file + ".tmp"));
    }
}
