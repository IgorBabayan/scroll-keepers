using System.Windows;
using System.Windows.Input;
using ScrollKeeper.Presentation.ViewModels.Addons;

namespace ScrollKeeper.Presentation.Views.Addons;

/// <summary>
/// Interaction logic for AddonDetailsWindow.xaml: the addon page.
/// </summary>
public partial class AddonDetailsWindow : Window
{
    private readonly AddonDetailsViewModel _viewModel;

    public AddonDetailsWindow(AddonDetailsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;

        Loaded += async (_, _) => await _viewModel.LoadAsync();
        Closed += (_, _) => _viewModel.OnClosed();
    }

    private void OnCloseExecuted(object sender, ExecutedRoutedEventArgs e) => Close();
}
