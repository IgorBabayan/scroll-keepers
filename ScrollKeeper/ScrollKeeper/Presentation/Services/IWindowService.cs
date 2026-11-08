using ScrollKeeper.Presentation.ViewModels.Addons;
using ScrollKeeper.Services.Addons;

namespace ScrollKeeper.Presentation.Services;

/// <summary>Opens the app's dialogs, owned by the active window.</summary>
public interface IWindowService
{
    /// <summary>Shows Settings modally. Returns <c>true</c> if the user saved.</summary>
    bool ShowSettings();

    /// <summary>Shows the page of an addon (modal).</summary>
    void ShowAddonDetails(AddonItemViewModel item);

    /// <summary>Shows the full-size pictures of an addon page, starting at <paramref name="index"/> (modal).</summary>
    void ShowImageViewer(string title, IReadOnlyList<ScreenshotViewModel> pictures, int index);

    /// <summary>Shows what removing an addon deletes. Returns <c>true</c> if the user confirmed.</summary>
    bool ConfirmRemoval(string addonName, RemovalPlan plan);
}
