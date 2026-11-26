using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ScrollKeeper.Presentation.Controls;
using ScrollKeeper.Presentation.ViewModels.Shell;

namespace ScrollKeeper.Presentation.Views;

/// <summary>
/// Interaction logic for MainWindow.axaml
/// </summary>
public partial class MainWindow : ChromeWindow
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // F5, Ctrl+, (Cmd+, on macOS) and Ctrl+Shift+N (Cmd+Shift+N).
        AddKeyBinding(Key.F5, KeyModifiers.None, viewModel.RefreshCommand);
        AddKeyBinding(Key.OemComma, CommandModifier, viewModel.OpenSettingsCommand);
        AddKeyBinding(Key.N, CommandModifier | KeyModifiers.Shift, viewModel.ActionCenter.ToggleCommand);

        // Scan the AddOns folder and load the ESOUI catalog once the window is on screen.
        Opened += async (_, _) => await viewModel.InitializeAsync();
    }

    #region Caption buttons

    private void OnMinimizeClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object? sender, RoutedEventArgs e) => ToggleMaximized();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    /// <summary>Maximize / Restore: icon and tooltip follow the window state.</summary>
    protected override void OnWindowStateChanged()
    {
        var isMaximized = WindowState == WindowState.Maximized;
        var text = isMaximized ? "Restore down" : "Maximize";

        if (this.TryFindResource(isMaximized ? "RestoreGeometry" : "MaximizeGeometry", ActualThemeVariant, out var geometry))
            MaximizeIcon.Data = geometry as Avalonia.Media.Geometry;
        ToolTip.SetTip(MaximizeButton, text);
        Avalonia.Automation.AutomationProperties.SetName(MaximizeButton, text);
    }

    #endregion
}
