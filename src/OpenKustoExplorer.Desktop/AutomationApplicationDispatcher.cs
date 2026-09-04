using System.ComponentModel;
using OpenKustoExplorer.Application.Automations;

namespace OpenKustoExplorer.Desktop;

/// <summary>
/// Dispatches a matched automation application action without propagating process-start failures.
/// </summary>
internal sealed class AutomationApplicationDispatcher
{
    private readonly IAutomationApplicationLauncher launcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="AutomationApplicationDispatcher"/> class.
    /// </summary>
    /// <param name="launcher">The host process launcher.</param>
    internal AutomationApplicationDispatcher(IAutomationApplicationLauncher launcher)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        this.launcher = launcher;
    }

    /// <summary>
    /// Launches the configured application when the matched action enables it.
    /// </summary>
    /// <param name="notification">The evaluated automation action.</param>
    /// <returns>A process-start error message, or <see langword="null"/> when no failure occurred.</returns>
    internal string? Dispatch(KustoAutomationNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (!notification.Settings.RunApplicationEnabled)
        {
            return null;
        }

        try
        {
            launcher.Launch(
                notification.Settings.ApplicationPath!,
                notification.ApplicationArguments);
            return null;
        }
        catch (Win32Exception exception)
        {
            return exception.Message;
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message;
        }
    }
}
