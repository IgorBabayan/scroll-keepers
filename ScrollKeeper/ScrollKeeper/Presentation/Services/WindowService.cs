using System.Windows;
using ScrollKeeper.Presentation.ViewModels.Addons;
using ScrollKeeper.Presentation.Views.Addons;
using ScrollKeeper.Presentation.Views.Settings;
using ScrollKeeper.Services.Addons;

namespace ScrollKeeper.Presentation.Services;

public sealed class WindowService(IServiceProvider services) : IWindowService
{
    public bool ShowSettings()
    {
        var window = services.GetRequiredService<SettingsWindow>();
        return ShowModal(window);
    }

    public void ShowAddonDetails(AddonItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var viewModel = ActivatorUtilities.CreateInstance<AddonDetailsViewModel>(services, item);
        ShowModal(new AddonDetailsWindow(viewModel));
    }

    public void ShowImageViewer(string title, IReadOnlyList<ScreenshotViewModel> pictures, int index)
    {
        ArgumentNullException.ThrowIfNull(pictures);
        if (pictures.Count == 0)
            return;

        var viewModel = ActivatorUtilities.CreateInstance<ImageViewerViewModel>(services, title, pictures, index);
        ShowModal(new ImageViewerWindow(viewModel));
    }

    public bool ConfirmRemoval(string addonName, RemovalPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return ShowModal(new ConfirmRemoveWindow(addonName, plan));
    }

    private static bool ShowModal(Window window)
    {
        var owner = WindowHelper.GetActiveWindow();
        if (owner is not null && !ReferenceEquals(owner, window))
            window.Owner = owner;
        else
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        return window.ShowDialog() == true;
    }
}
