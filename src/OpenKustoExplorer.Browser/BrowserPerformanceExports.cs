using System.Runtime.InteropServices.JavaScript;
using Avalonia.Threading;
using OpenKustoExplorer.Desktop;

namespace OpenKustoExplorer.Browser;

/// <summary>
/// Exposes constrained fixture actions to the loopback Browser performance runner.
/// </summary>
public static partial class BrowserPerformanceExports
{
    private static BrowserPerformanceFixture? fixture;
    private static WorkbenchView? workbench;

    /// <summary>
    /// Gets whether this Browser assembly was compiled without Debug instrumentation.
    /// </summary>
    /// <returns><see langword="true"/> for a Release build.</returns>
    [JSExport]
    public static bool IsReleaseBuild()
    {
#if DEBUG
        return false;
#else
        return true;
#endif
    }

    /// <summary>
    /// Prepares fixture data or focuses one real Avalonia interaction target.
    /// </summary>
    /// <param name="action">The constrained fixture action name.</param>
    /// <returns><see langword="true"/> when the action was accepted.</returns>
    [JSExport]
    public static bool Invoke(string action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        BrowserPerformanceFixture? currentFixture = fixture;
        WorkbenchView? currentWorkbench = workbench;
        if (currentFixture is null || currentWorkbench is null || !Dispatcher.UIThread.CheckAccess())
        {
            return false;
        }

        if (string.Equals(action, "prepare", StringComparison.Ordinal))
        {
            return currentFixture.Prepare();
        }

        return string.Equals(action, "large-editor", StringComparison.Ordinal)
            ? currentWorkbench.FocusPerformanceDocument(BrowserPerformanceFixture.GetLargeDocumentId())
            : currentWorkbench.FocusPerformanceTarget(action);
    }

    /// <summary>
    /// Connects the exported fixture bridge to the active Browser workbench.
    /// </summary>
    /// <param name="view">The active workbench view.</param>
    /// <param name="performanceFixture">The active synthetic fixture.</param>
    internal static void Initialize(WorkbenchView view, BrowserPerformanceFixture performanceFixture)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(performanceFixture);
        workbench = view;
        fixture = performanceFixture;
    }
}
