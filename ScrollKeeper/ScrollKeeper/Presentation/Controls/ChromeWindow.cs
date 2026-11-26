using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.Input;

namespace ScrollKeeper.Presentation.Controls;

/// <summary>What the custom title bar has to do on the current operating system.</summary>
public static class PlatformChrome
{
    /// <summary>
    /// Windows and Linux: the app draws Minimize / Maximize / Close itself.
    /// macOS keeps its own traffic-light buttons in the top-left corner.
    /// </summary>
    public static bool HasCaptionButtons { get; } = !OperatingSystem.IsMacOS();

    /// <summary>Room left of the title for the macOS traffic lights.</summary>
    public static Thickness TitleBarPadding { get; } = OperatingSystem.IsMacOS() ? new Thickness(72, 0, 0, 0) : default;
}

/// <summary>
/// Window with the app's own title bar (row 0 of every window, see Themes/WindowChrome.axaml):
/// <list type="bullet">
/// <item>Windows: the client area is extended over the frame; resizing, snapping and shadows stay native.</item>
/// <item>macOS: the client area is extended under the transparent title bar; the system traffic lights stay.</item>
/// <item>Linux: no system decorations; the title bar moves the window and the edges resize it.</item>
/// </list>
/// A window names its title bar <c>TitleBar</c>, its title <c>TitleText</c> and its outer border <c>RootBorder</c>.
/// </summary>
public class ChromeWindow : Window
{
    /// <summary>Width of the invisible resize frame on Linux.</summary>
    private const double ResizeFrame = 6;

    private static readonly Lazy<WindowIcon?> AppIcon = new(LoadIcon);

    /// <summary>Linux resize cursors, created once (UI thread only).</summary>
    private static readonly Dictionary<WindowEdge, Cursor> ResizeCursors = new();

    private Control? _titleBar;
    private TextBlock? _titleText;
    private Border? _rootBorder;
    private Thickness _rootBorderThickness = new(1);

    public ChromeWindow()
    {
        Icon = AppIcon.Value;

        if (OperatingSystem.IsWindows())
        {
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
            ExtendClientAreaTitleBarHeightHint = -1;
        }
        else if (OperatingSystem.IsMacOS())
        {
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.PreferSystemChrome;
            ExtendClientAreaTitleBarHeightHint = 32;
        }
        else
        {
            SystemDecorations = SystemDecorations.None;
            AddHandler(PointerPressedEvent, OnResizeFramePressed, RoutingStrategies.Tunnel);
            AddHandler(PointerMovedEvent, OnResizeFrameMoved, RoutingStrategies.Tunnel);
        }

        Activated += (_, _) => UpdateTitleForeground();
        Deactivated += (_, _) => UpdateTitleForeground();
    }

    /// <summary>Raised when the window is maximized, restored or minimized.</summary>
    protected virtual void OnWindowStateChanged()
    {
    }

    /// <summary>Closes the window with Esc.</summary>
    protected void CloseOnEscape() => AddKeyBinding(Key.Escape, KeyModifiers.None, new RelayCommand(Close));

    protected void AddKeyBinding(Key key, KeyModifiers modifiers, System.Windows.Input.ICommand command) =>
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(key, modifiers), Command = command });

    /// <summary>Ctrl on Windows and Linux, Cmd on macOS.</summary>
    protected static KeyModifiers CommandModifier =>
        Application.Current?.PlatformSettings?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;

    protected void ToggleMaximized() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    protected override void OnApplyTemplate(Avalonia.Controls.Primitives.TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        AttachChrome();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        AttachChrome();
        UpdateLayoutForState();
        UpdateTitleForeground();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty)
        {
            UpdateLayoutForState();
            OnWindowStateChanged();
        }
        else if (change.Property == OffScreenMarginProperty)
        {
            UpdateLayoutForState();
        }
    }

    private void AttachChrome()
    {
        if (_titleBar is null && this.FindControl<Control>("TitleBar") is { } titleBar)
        {
            _titleBar = titleBar;
            titleBar.PointerPressed += OnTitleBarPointerPressed;
        }

        _titleText ??= this.FindControl<TextBlock>("TitleText");

        if (_rootBorder is null && this.FindControl<Border>("RootBorder") is { } rootBorder)
        {
            _rootBorder = rootBorder;
            _rootBorderThickness = rootBorder.BorderThickness;
            // macOS draws its own rounded frame around the window.
            if (OperatingSystem.IsMacOS())
                _rootBorderThickness = default;
            rootBorder.BorderThickness = _rootBorderThickness;
        }
    }

    /// <summary>Drag the title bar to move the window; double-click it to maximize or restore.</summary>
    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Buttons in the title bar handle their own presses.
        if (e.Handled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (e.ClickCount == 2)
        {
            if (CanResize)
                ToggleMaximized();
        }
        else
        {
            BeginMoveDrag(e);
        }

        e.Handled = true;
    }

    /// <summary>
    /// A maximized window with an extended client area reaches past the screen edges on Windows: pad that away,
    /// and drop the 1px window border while maximized, as Windows does.
    /// </summary>
    private void UpdateLayoutForState()
    {
        if (_rootBorder is null)
            return;

        if (WindowState == WindowState.Maximized)
        {
            _rootBorder.BorderThickness = default;
            _rootBorder.Margin = OperatingSystem.IsWindows() ? OffScreenMargin : default;
        }
        else
        {
            _rootBorder.BorderThickness = _rootBorderThickness;
            _rootBorder.Margin = default;
        }
    }

    /// <summary>Resource references (not brush instances) so the title follows runtime theme changes.</summary>
    private void UpdateTitleForeground() =>
        _titleText?.Bind(TextBlock.ForegroundProperty,
            this.GetResourceObservable(IsActive ? "WindowForegroundBrush" : "TitleBarInactiveForegroundBrush"));

    // ===== Linux: resize from the window edges =====

    private WindowEdge? EdgeAt(Point point)
    {
        if (!CanResize || WindowState != WindowState.Normal)
            return null;

        var size = Bounds.Size;
        var left = point.X < ResizeFrame;
        var right = point.X > size.Width - ResizeFrame;
        var top = point.Y < ResizeFrame;
        var bottom = point.Y > size.Height - ResizeFrame;

        return (top, bottom, left, right) switch
        {
            (true, _, true, _) => WindowEdge.NorthWest,
            (true, _, _, true) => WindowEdge.NorthEast,
            (_, true, true, _) => WindowEdge.SouthWest,
            (_, true, _, true) => WindowEdge.SouthEast,
            (true, _, _, _) => WindowEdge.North,
            (_, true, _, _) => WindowEdge.South,
            (_, _, true, _) => WindowEdge.West,
            (_, _, _, true) => WindowEdge.East,
            _ => null
        };
    }

    private void OnResizeFrameMoved(object? sender, PointerEventArgs e)
    {
        if (EdgeAt(e.GetPosition(this)) is { } edge)
        {
            if (!ResizeCursors.TryGetValue(edge, out var cursor))
                ResizeCursors[edge] = cursor = new Cursor(edge switch
                {
                    WindowEdge.North => StandardCursorType.TopSide,
                    WindowEdge.South => StandardCursorType.BottomSide,
                    WindowEdge.West => StandardCursorType.LeftSide,
                    WindowEdge.East => StandardCursorType.RightSide,
                    WindowEdge.NorthWest => StandardCursorType.TopLeftCorner,
                    WindowEdge.NorthEast => StandardCursorType.TopRightCorner,
                    WindowEdge.SouthWest => StandardCursorType.BottomLeftCorner,
                    _ => StandardCursorType.BottomRightCorner
                });
            Cursor = cursor;
        }
        else if (Cursor is not null)
        {
            ClearValue(CursorProperty);
        }
    }

    private void OnResizeFramePressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || EdgeAt(e.GetPosition(this)) is not { } edge)
            return;

        BeginResizeDrag(edge, e);
        e.Handled = true;
    }

    private static WindowIcon? LoadIcon()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://ScrollKeeper/Assets/App.png"));
            return new WindowIcon(stream);
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or InvalidOperationException)
        {
            return null;
        }
    }
}
