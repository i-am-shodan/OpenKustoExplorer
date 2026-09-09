using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;

namespace OpenKustoExplorer.Desktop;

/// <summary>
/// Presents bounded, non-blocking notifications owned by the main window.
/// </summary>
internal sealed class DesktopNotificationService
{
    private readonly WindowNotificationManager notificationManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="DesktopNotificationService"/> class.
    /// </summary>
    /// <param name="host">The window that owns notifications.</param>
    internal DesktopNotificationService(TopLevel host)
    {
        ArgumentNullException.ThrowIfNull(host);
        notificationManager = new WindowNotificationManager(host)
        {
            MaxItems = 4,
            Position = NotificationPosition.BottomRight,
        };
    }

    /// <summary>
    /// Shows an informational desktop notification.
    /// </summary>
    /// <param name="title">The concise notification title.</param>
    /// <param name="message">The notification detail.</param>
    internal void ShowInformation(string title, string message)
    {
        Show(title, message, NotificationType.Information, TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// Shows an error desktop notification.
    /// </summary>
    /// <param name="title">The concise failure title.</param>
    /// <param name="message">The recoverable failure detail.</param>
    internal void ShowError(string title, string message)
    {
        Show(title, message, NotificationType.Error, TimeSpan.FromSeconds(12));
    }

    private void Show(
        string title,
        string message,
        NotificationType type,
        TimeSpan expiration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        void ShowCore() => notificationManager.Show(new Notification(title, message, type, expiration));

        if (Dispatcher.UIThread.CheckAccess())
        {
            ShowCore();
        }
        else
        {
            Dispatcher.UIThread.Post(ShowCore);
        }
    }
}
