using Avalonia.Input;
using ScrollKeeper.Presentation.Controls;
using ScrollKeeper.Presentation.ViewModels.Settings;

namespace ScrollKeeper.Presentation.Views.Settings;

/// <summary>
/// Interaction logic for SettingsWindow.axaml
/// </summary>
public partial class SettingsWindow : ChromeWindow
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;

        AddKeyBinding(Key.Escape, KeyModifiers.None, _viewModel.CancelCommand);

        _viewModel.CloseRequested += OnCloseRequested;
        Closed += OnClosed;
    }

    /// <summary>Closes the dialog; the result (saved or not) is returned by <c>ShowDialog</c>.</summary>
    private void OnCloseRequested(object? sender, bool saved)
    {
        if (IsVisible)
            Close(saved);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.CloseRequested -= OnCloseRequested;
        _viewModel.OnClosed();
    }
}
