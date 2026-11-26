using ScrollKeeper.Presentation.ViewModels.Addons;
using ScrollKeeper.Services.Addons;

namespace ScrollKeeper.Presentation.Services;

/// <summary>Opens the app's dialogs, owned by the active window. Dialogs are modal; the tasks end when they close.</summary>
public interface IWindowService
{
    /// <summary>Shows Settings modally. Returns <c>true</c> if the user saved.</summary>
    Task<bool> ShowSettingsAsync();

    /// <summary>Shows the page of an addon (modal).</summary>
    Task ShowAddonDetailsAsync(AddonItemViewModel item);

    /// <summary>Shows the full-size pictures of an addon page, starting at <paramref name="index"/> (modal).</summary>
    Task ShowImageViewerAsync(string title, IReadOnlyList<ScreenshotViewModel> pictures, int index);

    /// <summary>Shows what removing an addon deletes. Returns <c>true</c> if the user confirmed.</summary>
    Task<bool> ConfirmRemovalAsync(string addonName, RemovalPlan plan);
}
