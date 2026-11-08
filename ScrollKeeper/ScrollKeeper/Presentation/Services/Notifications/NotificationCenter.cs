using System.Collections.ObjectModel;
using ScrollKeeper.Presentation.Services.Toasts;

namespace ScrollKeeper.Presentation.Services.Notifications;

public sealed class NotificationCenter : ObservableObject, INotificationCenter
{
    /// <summary>Older notifications are dropped beyond this count.</summary>
    private const int MaxCount = 50;

    private readonly ObservableCollection<Notification> _notifications = [];

    /// <summary>Key → signature of a state notification the user removed, so a refresh does not bring it back.</summary>
    private readonly Dictionary<string, string> _dismissed = new(StringComparer.Ordinal);

    public NotificationCenter()
    {
        Notifications = new ReadOnlyObservableCollection<Notification>(_notifications);
    }

    public ReadOnlyObservableCollection<Notification> Notifications { get; }

    public int UnreadCount => _notifications.Count(n => n.IsUnread);

    public bool HasUnread => UnreadCount > 0;

    public bool IsEmpty => _notifications.Count == 0;

    public Notification Add(string title, ToastSeverity severity = ToastSeverity.Info, string? details = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var notification = new Notification(null, title, severity, details);
        Insert(notification);
        return notification;
    }

    public Notification? Set(
        string key,
        string title,
        ToastSeverity severity = ToastSeverity.Info,
        string? details = null,
        string? actionText = null,
        Action? action = null,
        bool isNews = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var existing = Find(key);
        var candidate = new Notification(key, title, severity, details);

        if (existing is null)
        {
            if (_dismissed.TryGetValue(key, out var dismissed) && (!isNews || dismissed == candidate.Signature))
            {
                _dismissed[key] = candidate.Signature;
                return null;
            }

            _dismissed.Remove(key);
            candidate.SetAction(actionText, action);
            Insert(candidate);
            return candidate;
        }

        existing.SetAction(actionText, action);
        if (existing.Signature == candidate.Signature)
            return existing;

        existing.Title = title;
        existing.Severity = severity;
        existing.Details = details;
        if (!isNews)
            return existing;

        // The state changed: show it as new.
        existing.Timestamp = candidate.Timestamp;
        existing.IsUnread = true;

        var index = _notifications.IndexOf(existing);
        if (index > 0)
            _notifications.Move(index, 0);

        RaiseCountsChanged();
        return existing;
    }

    public void Remove(string key)
    {
        _dismissed.Remove(key);
        if (Find(key) is { } notification)
            RemoveCore(notification);
    }

    public void Dismiss(Notification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (notification.Key is { } key)
            _dismissed[key] = notification.Signature;
        RemoveCore(notification);
    }

    public void Clear()
    {
        foreach (var notification in _notifications.ToList())
            Dismiss(notification);
    }

    public void MarkAllRead()
    {
        foreach (var notification in _notifications)
            notification.IsUnread = false;
        RaiseCountsChanged();
    }

    private Notification? Find(string key) =>
        _notifications.FirstOrDefault(n => string.Equals(n.Key, key, StringComparison.Ordinal));

    private void Insert(Notification notification)
    {
        _notifications.Insert(0, notification);
        while (_notifications.Count > MaxCount)
            _notifications.RemoveAt(_notifications.Count - 1);
        RaiseCountsChanged();
    }

    private void RemoveCore(Notification notification)
    {
        if (_notifications.Remove(notification))
            RaiseCountsChanged();
    }

    private void RaiseCountsChanged()
    {
        OnPropertyChanged(nameof(UnreadCount));
        OnPropertyChanged(nameof(HasUnread));
        OnPropertyChanged(nameof(IsEmpty));
    }
}
