using Avalonia.Input;
using Avalonia.Interactivity;
using ScrollKeeper.Presentation.Controls;
using ScrollKeeper.Presentation.ViewModels.Addons;

namespace ScrollKeeper.Presentation.Views.Addons;

/// <summary>
/// Interaction logic for ImageViewerWindow.axaml: full-size pictures of an addon page.
/// </summary>
public partial class ImageViewerWindow : ChromeWindow
{
    public ImageViewerWindow(ImageViewerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        CloseOnEscape();
        AddKeyBinding(Key.Left, KeyModifiers.None, viewModel.PreviousCommand);
        AddKeyBinding(Key.Right, KeyModifiers.None, viewModel.NextCommand);

        Opened += async (_, _) => await viewModel.LoadAsync();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
