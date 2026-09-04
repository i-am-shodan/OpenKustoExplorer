namespace OpenKustoExplorer.Application.Automations;

/// <summary>
/// Supplies one evaluated automation notification to a host-provided channel dispatcher.
/// </summary>
public sealed class KustoAutomationNotificationEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoAutomationNotificationEventArgs"/> class.
    /// </summary>
    /// <param name="notification">The evaluated notification.</param>
    public KustoAutomationNotificationEventArgs(KustoAutomationNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        Notification = notification;
    }

    /// <summary>
    /// Gets the evaluated notification.
    /// </summary>
    public KustoAutomationNotification Notification { get; }
}
