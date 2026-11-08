using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ScrollKeeper.Presentation.ViewModels.Shell;

namespace ScrollKeeper.Presentation.Views;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Scan the AddOns folder and load the ESOUI catalog once the window is on screen.
        Loaded += async (_, _) => await viewModel.InitializeAsync();

        StateChanged += (_, _) => UpdateMaximizedLayout();
        // Resource references (not brush instances) so the title follows runtime theme changes.
        Activated += (_, _) => TitleText.SetResourceReference(TextBlock.ForegroundProperty, "WindowForegroundBrush");
        Deactivated += (_, _) => TitleText.SetResourceReference(TextBlock.ForegroundProperty, "TitleBarInactiveForegroundBrush");
    }

    #region Caption buttons

    private void OnMinimizeExecuted(object sender, ExecutedRoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void OnMaximizeExecuted(object sender, ExecutedRoutedEventArgs e) => SystemCommands.MaximizeWindow(this);

    private void OnRestoreExecuted(object sender, ExecutedRoutedEventArgs e) => SystemCommands.RestoreWindow(this);

    private void OnCloseExecuted(object sender, ExecutedRoutedEventArgs e) => SystemCommands.CloseWindow(this);

    #endregion

    #region Maximized layout

    private const int SM_CXSIZEFRAME = 32;
    private const int SM_CYSIZEFRAME = 33;
    private const int SM_CXPADDEDBORDER = 92;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    /// <summary>
    /// A maximized window is larger than the screen by its (invisible) resize frame on every side. With the native
    /// title bar removed, that frame would cut off the edges of the content: pad it away while maximized, and drop the
    /// 1px window border, as Windows does.
    /// </summary>
    private void UpdateMaximizedLayout()
    {
        if (WindowState == WindowState.Maximized)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var padded = GetSystemMetrics(SM_CXPADDEDBORDER);
            var x = (GetSystemMetrics(SM_CXSIZEFRAME) + padded) / dpi.DpiScaleX;
            var y = (GetSystemMetrics(SM_CYSIZEFRAME) + padded) / dpi.DpiScaleY;

            RootBorder.BorderThickness = new Thickness(0);
            RootBorder.Margin = new Thickness(x, y, x, y);
        }
        else
        {
            RootBorder.BorderThickness = new Thickness(1);
            RootBorder.Margin = new Thickness(0);
        }
    }

    #endregion
}
