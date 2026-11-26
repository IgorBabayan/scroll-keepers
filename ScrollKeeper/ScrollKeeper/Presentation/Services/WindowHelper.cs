using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace ScrollKeeper.Presentation.Services;

internal static class WindowHelper
{
    /// <summary>The currently active window, falling back to the main window. Used as dialog owner.</summary>
    public static Window? GetActiveWindow()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;

        return desktop.Windows.FirstOrDefault(w => w.IsActive) ?? desktop.MainWindow;
    }
}
