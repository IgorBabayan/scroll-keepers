using Avalonia;
using Avalonia.Controls;
using ScrollKeeper.Presentation.Services.Toasts;

namespace ScrollKeeper.Presentation.Views.Toasts;

/// <summary>
/// Overlay that shows the toasts of <see cref="IToastService"/>.
/// Every window that can be in front hosts one; a host hides itself while one of its window's
/// owned dialogs (e.g. Settings) is open, so toasts appear only once, in the frontmost window.
/// </summary>
public partial class ToastHost : UserControl
{
    private Window? _window;

    public ToastHost()
    {
        InitializeComponent();

        if (Design.IsDesignMode)
            return;

        // Created by XAML, so constructor injection is not available here.
        DataContext = App.Services.GetRequiredService<IToastService>();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is null)
            return;

        _window.Activated += OnWindowActivationChanged;
        _window.Deactivated += OnWindowActivationChanged;
        UpdateVisibility();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (_window is null)
            return;

        _window.Activated -= OnWindowActivationChanged;
        _window.Deactivated -= OnWindowActivationChanged;
        _window = null;
    }

    private void OnWindowActivationChanged(object? sender, EventArgs e) => UpdateVisibility();

    private void UpdateVisibility()
    {
        if (_window is null)
            return;

        // An open owned dialog hosts the toasts itself (it is in front of this window).
        var hasOpenDialog = !_window.IsActive && _window.OwnedWindows.Any(w => w.IsVisible);
        IsVisible = !hasOpenDialog;
    }
}
