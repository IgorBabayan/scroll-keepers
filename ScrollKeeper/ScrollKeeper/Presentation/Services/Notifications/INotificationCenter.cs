using System.Collections.ObjectModel;
using System.ComponentModel;
using ScrollKeeper.Presentation.Services.Toasts;

namespace ScrollKeeper.Presentation.Services.Notifications;

/// <summary>
/// Notifications shown in the action center (the bell in the title bar). Toasts are separate and transient;
/// the action center keeps what still needs attention: available updates, failures, a missing AddOns folder.
/// Must be used from the UI thread.
/// </summary>
public interface INotificationCenter : INotifyPropertyChanged
{
    /// <summary>Newest first.</summary>
    ReadOnlyObservableCollection<Notification> Notifications { get; }

    int UnreadCount { get; }

    bool HasUnread { get; }

    bool IsEmpty { get; }

    /// <summary>Adds a one-off event (e.g. "Could not install X").</summary>
    Notification Add(string title, ToastSeverity severity = ToastSeverity.Info, string? details = null);

    /// <summary>
    /// Shows the current state for <paramref name="key"/>, replacing the previous notification with that key.
    /// An unchanged state keeps its read flag, and a state the user removed is not shown again until it changes.
    /// </summary>
    /// <param name="isNews">
    /// <c>false</c> when the change is not worth the user's attention (e.g. fewer updates left after updating):
    /// the text is updated in place without marking it unread, and a removed notification stays removed.
    /// </param>
    /// <returns>The notification, or <c>null</c> if the user removed this exact state.</returns>
    Notification? Set(
        string key,
        string title,
        ToastSeverity severity = ToastSeverity.Info,
        string? details = null,
        string? actionText = null,
        Action? action = null,
        bool isNews = true);

    /// <summary>The state for <paramref name="key"/> is resolved: removes its notification.</summary>
    void Remove(string key);

    /// <summary>The user removed a notification.</summary>
    void Dismiss(Notification notification);

    /// <summary>The user removed every notification.</summary>
    void Clear();

    void MarkAllRead();
}
