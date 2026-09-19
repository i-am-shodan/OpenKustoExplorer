using OpenKustoExplorer.Application.Automations;

namespace OpenKustoExplorer.Desktop;

/// <summary>
/// Delivers workbench automation notifications through host-specific channels.
/// </summary>
public interface IWorkbenchAutomationNotificationDispatcher
{
    /// <summary>
    /// Delivers an evaluated automation notification.
    /// </summary>
    /// <param name="notification">The evaluated notification.</param>
    /// <param name="cancellationToken">Cancels delivery during host shutdown.</param>
    /// <returns>A task that completes when enabled delivery channels have been attempted.</returns>
    public Task DispatchAsync(
        KustoAutomationNotification notification,
        CancellationToken cancellationToken);
}
