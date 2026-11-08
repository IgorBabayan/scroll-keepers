using ScrollKeeper.Presentation.Services.Notifications;

namespace ScrollKeeper.Presentation.ViewModels.Shell;

/// <summary>
/// The action center flyout opened from the bell in the main window's title bar.
/// Notifications stay unread (and keep their dot) while the flyout is open; closing it marks them read.
/// </summary>
public sealed partial class ActionCenterViewModel : ViewModelBase
{
    public ActionCenterViewModel(INotificationCenter center)
    {
        Center = center;
    }

    public INotificationCenter Center { get; }

    [ObservableProperty]
    private bool _isOpen;

    partial void OnIsOpenChanged(bool value)
    {
        if (!value)
            Center.MarkAllRead();
    }

    /// <summary>Runs the notification's action (if any) and closes the flyout.</summary>
    [RelayCommand]
    private void Activate(Notification? notification)
    {
        if (notification?.ActionCommand is not { } command)
            return;

        IsOpen = false;
        if (command.CanExecute(null))
            command.Execute(null);
    }

    [RelayCommand]
    private void Dismiss(Notification? notification)
    {
        if (notification is not null)
            Center.Dismiss(notification);
    }

    [RelayCommand]
    private void ClearAll() => Center.Clear();

    [RelayCommand]
    private void Toggle() => IsOpen = !IsOpen;
}
