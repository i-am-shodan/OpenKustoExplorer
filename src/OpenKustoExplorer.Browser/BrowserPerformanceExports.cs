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
    private static int largeDocumentIndex;
    private static int resultQueryIndex;
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

        if (string.Equals(action, "run-query", StringComparison.Ordinal))
        {
            return currentWorkbench.RunPerformanceQuery();
        }

        if (string.Equals(action, "repair-query", StringComparison.Ordinal))
        {
            return currentWorkbench.SetPerformanceQuery(BrowserPerformanceFixture.GetRepairQueryText());
        }

        if (string.Equals(action, "trend-query", StringComparison.Ordinal))
        {
            return currentWorkbench.SetPerformanceQuery(BrowserPerformanceFixture.GetTrendQueryText());
        }

        if (string.Equals(action, "render-timechart", StringComparison.Ordinal))
        {
            return currentWorkbench.RenderPerformanceTimeChart();
        }

        if (string.Equals(action, "open-custom-time-range", StringComparison.Ordinal))
        {
            return currentWorkbench.OpenPerformanceCustomTimeRange();
        }

        if (string.Equals(action, "record-large-result", StringComparison.Ordinal))
        {
            return currentWorkbench.StartPerformanceRecordedQuery(
                BrowserPerformanceFixture.LargeResultQueryText,
                BrowserPerformanceFixture.ResultRowCount);
        }

        if (string.Equals(action, "large-result-query", StringComparison.Ordinal))
        {
            return currentWorkbench.SetPerformanceQuery(
                BrowserPerformanceFixture.LargeResultQueryText);
        }

        if (string.Equals(action, "next-large-result-query", StringComparison.Ordinal))
        {
            resultQueryIndex++;
            return currentWorkbench.SetPerformanceQuery(
                BrowserPerformanceFixture.GetLargeResultQueryText(resultQueryIndex));
        }

        if (string.Equals(action, "query-document", StringComparison.Ordinal))
        {
            return currentWorkbench.FocusPerformanceDocument(
                BrowserPerformanceFixture.GetDefaultDocumentId());
        }

        if (string.Equals(action, "next-large-editor", StringComparison.Ordinal))
        {
            IReadOnlyList<Guid> documentIds = BrowserPerformanceFixture.GetLargeDocumentIds();
            largeDocumentIndex = (largeDocumentIndex + 1) % documentIds.Count;
            return currentWorkbench.FocusPerformanceDocument(documentIds[largeDocumentIndex]);
        }

        if (string.Equals(action, "large-editor", StringComparison.Ordinal))
        {
            largeDocumentIndex = 0;
            return currentWorkbench.FocusPerformanceDocument(BrowserPerformanceFixture.GetLargeDocumentId());
        }

        return InvokeTargetAction(currentWorkbench, action)
            ?? currentWorkbench.FocusPerformanceTarget(action);
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
        largeDocumentIndex = 0;
        resultQueryIndex = 0;
    }

    private static bool? InvokeTargetAction(WorkbenchView currentWorkbench, string action)
    {
        const string ResultPagePrefix = "result-page-";
        if (action.StartsWith(ResultPagePrefix, StringComparison.Ordinal))
        {
            return action[ResultPagePrefix.Length..] switch
            {
                "next" => currentWorkbench.ActivatePerformanceResultPage(next: true),
                "previous" => currentWorkbench.ActivatePerformanceResultPage(next: false),
                _ => false,
            };
        }

        const string ActivatePrefix = "activate-";
        if (action.StartsWith(ActivatePrefix, StringComparison.Ordinal))
        {
            return currentWorkbench.ActivatePerformanceTarget(action[ActivatePrefix.Length..]);
        }

        return null;
    }
}
