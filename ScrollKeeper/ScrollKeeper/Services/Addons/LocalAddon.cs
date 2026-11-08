namespace ScrollKeeper.Services.Addons;

/// <summary>An addon folder found directly inside the AddOns folder.</summary>
/// <param name="FolderName">Folder name, which is also the addon's name for the game.</param>
/// <param name="Path">Full path of the folder.</param>
/// <param name="Manifest">Parsed manifest header.</param>
public sealed record LocalAddon(string FolderName, string Path, AddonManifest Manifest);
