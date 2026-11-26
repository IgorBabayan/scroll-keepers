using Avalonia.Interactivity;
using ScrollKeeper.Presentation.Controls;
using ScrollKeeper.Presentation.ViewModels.Addons;

namespace ScrollKeeper.Presentation.Views.Addons;

/// <summary>
/// Interaction logic for AddonDetailsWindow.axaml: the addon page.
/// </summary>
public partial class AddonDetailsWindow : ChromeWindow
{
    private readonly AddonDetailsViewModel _viewModel;

    public AddonDetailsWindow(AddonDetailsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;

        CloseOnEscape();

        Opened += async (_, _) => await _viewModel.LoadAsync();
        Closed += (_, _) => _viewModel.OnClosed();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
