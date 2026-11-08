using ScrollKeeper.Presentation.ViewModels.Addons;

namespace ScrollKeeper.Presentation.Services;

/// <summary>Opens the app's dialogs, owned by the active window.</summary>
public interface IWindowService
{
    /// <summary>Shows Settings modally. Returns <c>true</c> if the user saved.</summary>
    bool ShowSettings();

    /// <summary>Shows the page of an addon (modal).</summary>
    void ShowAddonDetails(AddonItemViewModel item);
}
