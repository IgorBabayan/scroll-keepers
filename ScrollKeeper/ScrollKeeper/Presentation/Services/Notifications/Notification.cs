using System.Globalization;
using ScrollKeeper.Presentation.Services.Toasts;

namespace ScrollKeeper.Presentation.Services.Notifications;

/// <summary>
/// An entry of the action center. Unlike a toast it stays until the user removes it or the state it describes
/// goes away (e.g. "3 updates available" disappears once everything is updated).
/// </summary>
public sealed partial class Notification : ObservableObject
{
    internal Notification(string? key, string title, ToastSeverity severity, string? details)
    {
        Key = key;
        _title = title;
        _severity = severity;
        _details = details;
        _timestamp = DateTime.Now;
    }

    /// <summary>
    /// Identifies a state notification (one per key, replaced in place); <c>null</c> for one-off events.
    /// </summary>
    public string? Key { get; }

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private ToastSeverity _severity;

    /// <summary>Optional second line under the title.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetails))]
    private string? _details;

    public bool HasDetails => !string.IsNullOrWhiteSpace(Details);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimeText))]
    private DateTime _timestamp;

    /// <summary>Time for today's notifications, date and time for older ones.</summary>
    public string TimeText => Timestamp.Date == DateTime.Today
        ? Timestamp.ToString("t", CultureInfo.CurrentCulture)
        : Timestamp.ToString("g", CultureInfo.CurrentCulture);

    /// <summary>Unread notifications are counted on the title bar badge and marked with a dot.</summary>
    [ObservableProperty]
    private bool _isUnread = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAction))]
    private string? _actionText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAction))]
    private IRelayCommand? _actionCommand;

    public bool HasAction => ActionCommand is not null && !string.IsNullOrWhiteSpace(ActionText);

    /// <summary>Text shown as a link (e.g. "Update all"); clicking the notification runs <paramref name="action"/>.</summary>
    public void SetAction(string? text, Action? action)
    {
        ActionText = action is null ? null : text;
        ActionCommand = action is null ? null : new RelayCommand(action);
    }

    internal string Signature => $"{Severity}\n{Title}\n{Details}";
}
