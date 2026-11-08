using System.Windows;
using System.Windows.Input;
using ScrollKeeper.Presentation.ViewModels.Addons;

namespace ScrollKeeper.Presentation.Views.Addons;

/// <summary>
/// Interaction logic for ImageViewerWindow.xaml: full-size pictures of an addon page.
/// </summary>
public partial class ImageViewerWindow : Window
{
    public ImageViewerWindow(ImageViewerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        Loaded += async (_, _) => await viewModel.LoadAsync();
    }

    private void OnCloseExecuted(object sender, ExecutedRoutedEventArgs e) => Close();
}
