using Avalonia.Platform.Storage;

namespace ScrollKeeper.Presentation.Services;

/// <summary>The folder dialog of the operating system (Windows, macOS, or the GTK / portal dialog on Linux).</summary>
public sealed class FolderPickerService : IFolderPickerService
{
    public async Task<string?> PickFolderAsync(string title, string? initialDirectory = null)
    {
        if (WindowHelper.GetActiveWindow() is not { } owner)
            return null;

        var storage = owner.StorageProvider;
        if (!storage.CanPickFolder)
            return null;

        IStorageFolder? start = null;
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
            start = await storage.TryGetFolderFromPathAsync(initialDirectory);

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = start
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
