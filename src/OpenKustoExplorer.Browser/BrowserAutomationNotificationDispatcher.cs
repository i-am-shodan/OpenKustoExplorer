using OpenKustoExplorer.Application.Automations;
using OpenKustoExplorer.Desktop;

namespace OpenKustoExplorer.Browser;

/// <summary>
/// Delivers enabled automation notifications as in-app browser toasts.
/// </summary>
internal sealed class BrowserAutomationNotificationDispatcher : IWorkbenchAutomationNotificationDispatcher
{
    /// <inheritdoc />
    public Task DispatchAsync(
        KustoAutomationNotification notification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        cancellationToken.ThrowIfCancellationRequested();

        if (notification.Settings.DesktopEnabled)
        {
            BrowserInterop.ShowToast(notification.Title, notification.Message);
        }

        return Task.CompletedTask;
    }
}
