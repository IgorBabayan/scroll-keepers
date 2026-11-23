namespace ScrollKeeper.Services.Addons;

/// <summary>What removing one addon deletes, shown to the user before anything is deleted.</summary>
/// <param name="AddonId">ESOUI id of the removed addon, or <c>null</c> for an addon ESOUI does not know.</param>
/// <param name="AddonFolders">The addon's own folders in AddOns.</param>
/// <param name="UnusedDependencies">Libraries it required that no other installed addon uses any more.</param>
/// <param name="KeptDependencies">Libraries (and bundled folders) it required that other addons still use.</param>
/// <param name="SavedVariablesFiles">Full paths of the settings files (<c>SavedVariables\Name.lua</c>) to delete.</param>
/// <param name="SavedVariablesCleanups">
/// Other settings files that also hold the removed addons' saved-variable globals next to globals of other addons;
/// only those globals are cut out of them.
/// </param>
/// <param name="RequiredBy">Installed addons that require the removed addon and will stop loading without it.</param>
public sealed record RemovalPlan(
    string? AddonId,
    IReadOnlyList<string> AddonFolders,
    IReadOnlyList<string> UnusedDependencies,
    IReadOnlyList<string> KeptDependencies,
    IReadOnlyList<string> SavedVariablesFiles,
    IReadOnlyList<SavedVariablesCleanup> SavedVariablesCleanups,
    IReadOnlyList<string> RequiredBy)
{
    public IEnumerable<string> AllFolders => AddonFolders.Concat(UnusedDependencies);

    /// <summary>Whether any saved settings are deleted, whole files or globals cut out of other files.</summary>
    public bool RemovesSavedVariables => SavedVariablesFiles.Count > 0 || SavedVariablesCleanups.Count > 0;
}

/// <summary>Saved-variable globals of a removed addon found in a settings file that also holds other globals.</summary>
/// <param name="File">Full path of the <c>SavedVariables\*.lua</c> file.</param>
/// <param name="Globals">Global names to cut out of it.</param>
public sealed record SavedVariablesCleanup(string File, IReadOnlyList<string> Globals);

/// <summary>Removes an addon, its settings and the libraries nothing else needs.</summary>
public interface IAddonUninstaller
{
    /// <summary>Works out what removing <paramref name="addon"/> deletes; reads the AddOns folder only.</summary>
    RemovalPlan Plan(string addonsFolder, InstalledAddon addon);

    /// <summary>Deletes everything listed in <paramref name="plan"/> and forgets the install records.</summary>
    void Remove(string addonsFolder, RemovalPlan plan);
}
