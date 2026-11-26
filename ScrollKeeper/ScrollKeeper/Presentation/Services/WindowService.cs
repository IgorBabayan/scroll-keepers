using Avalonia.Controls;
using ScrollKeeper.Presentation.ViewModels.Addons;
using ScrollKeeper.Presentation.Views.Addons;
using ScrollKeeper.Presentation.Views.Settings;
using ScrollKeeper.Services.Addons;

namespace ScrollKeeper.Presentation.Services;

public sealed class WindowService(IServiceProvider services) : IWindowService
{
    public Task<bool> ShowSettingsAsync()
    {
        var window = services.GetRequiredService<SettingsWindow>();
        return ShowModalAsync(window);
    }

    public Task ShowAddonDetailsAsync(AddonItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var viewModel = ActivatorUtilities.CreateInstance<AddonDetailsViewModel>(services, item);
        return ShowModalAsync(new AddonDetailsWindow(viewModel));
    }

    public Task ShowImageViewerAsync(string title, IReadOnlyList<ScreenshotViewModel> pictures, int index)
    {
        ArgumentNullException.ThrowIfNull(pictures);
        if (pictures.Count == 0)
            return Task.CompletedTask;

        var viewModel = ActivatorUtilities.CreateInstance<ImageViewerViewModel>(services, title, pictures, index);
        return ShowModalAsync(new ImageViewerWindow(viewModel));
    }

    public Task<bool> ConfirmRemovalAsync(string addonName, RemovalPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return ShowModalAsync(new ConfirmRemoveWindow(addonName, plan));
    }

    /// <summary>Shows <paramref name="window"/> modally; the result is the value passed to <c>Close(result)</c>.</summary>
    private static async Task<bool> ShowModalAsync(Window window)
    {
        var owner = WindowHelper.GetActiveWindow();
        if (owner is not null && !ReferenceEquals(owner, window))
            return await window.ShowDialog<bool>(owner);

        // No window to own the dialog (should not happen): show it on its own and wait until it closes.
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var closed = new TaskCompletionSource();
        window.Closed += (_, _) => closed.TrySetResult();
        window.Show();
        await closed.Task;
        return false;
    }
}
