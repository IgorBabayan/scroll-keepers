namespace ScrollKeeper.Presentation.ViewModels.Addons;

/// <summary>Actions an addon row or the addon page asks the shell to perform.</summary>
public interface IAddonActions
{
    /// <summary>Installs or updates the addon; progress is shown on the item itself.</summary>
    Task InstallAsync(AddonItemViewModel item);

    /// <summary>Opens the addon page dialog.</summary>
    void OpenDetails(AddonItemViewModel item);
}
