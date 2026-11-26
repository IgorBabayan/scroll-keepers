using Avalonia;
using Avalonia.Controls;
using ScrollKeeper.Presentation.Services.Toasts;

namespace ScrollKeeper.Presentation.Controls;

/// <summary>Icon and color of a toast or notification: info, success, warning or error.</summary>
public sealed class SeverityIcon : Icon
{
    public static readonly StyledProperty<ToastSeverity> SeverityProperty =
        AvaloniaProperty.Register<SeverityIcon, ToastSeverity>(nameof(Severity));

    public SeverityIcon()
    {
        Apply(ToastSeverity.Info);
    }

    public ToastSeverity Severity
    {
        get => GetValue(SeverityProperty);
        set => SetValue(SeverityProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SeverityProperty)
            Apply(Severity);
    }

    /// <summary>Resource references (not brush instances), so the icon follows runtime theme changes.</summary>
    private void Apply(ToastSeverity severity)
    {
        var (geometry, brush) = severity switch
        {
            ToastSeverity.Success => ("SuccessGeometry", "SuccessForegroundBrush"),
            ToastSeverity.Warning => ("WarningGeometry", "WarningForegroundBrush"),
            ToastSeverity.Error => ("ErrorGeometry", "ErrorForegroundBrush"),
            _ => ("InfoGeometry", "AccentBrush")
        };

        Bind(DataProperty, this.GetResourceObservable(geometry));
        Bind(ForegroundProperty, this.GetResourceObservable(brush));
    }
}
