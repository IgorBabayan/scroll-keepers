namespace ScrollKeeper.Services.Addons;

/// <summary>Finds the addons installed in an AddOns folder.</summary>
public interface ILocalAddonScanner
{
    /// <summary>Addons directly inside <paramref name="addonsFolder"/>; empty if the folder does not exist.</summary>
    IReadOnlyList<LocalAddon> Scan(string addonsFolder);
}
