using System.Runtime.InteropServices.JavaScript;

namespace OpenKustoExplorer.Browser;

/// <summary>
/// Provides the minimal browser navigation operations required by the workbench.
/// </summary>
internal static partial class BrowserInterop
{
    /// <summary>
    /// Removes the startup splash after the Avalonia workbench is ready to paint.
    /// </summary>
    [JSImport("globalThis.openKustoExplorerCompleteStartup")]
    internal static partial void CompleteStartup();

    /// <summary>
    /// Adds one named Browser startup milestone to the Performance Timeline.
    /// </summary>
    /// <param name="name">The stable milestone name without the application prefix.</param>
    [JSImport("globalThis.openKustoExplorerMarkPerformance")]
    internal static partial void MarkPerformance(string name);

    /// <summary>
    /// Starts one correlated Browser performance operation.
    /// </summary>
    /// <param name="operationId">The invariant operation identifier.</param>
    /// <param name="operationName">The stable operation name.</param>
    /// <param name="itemCount">The optional item count.</param>
    [JSImport("globalThis.openKustoExplorerStartPerformanceOperation")]
    internal static partial void StartPerformanceOperation(
        string operationId,
        string operationName,
        int itemCount);

    /// <summary>
    /// Completes one correlated Browser performance operation.
    /// </summary>
    /// <param name="operationId">The invariant operation identifier.</param>
    /// <param name="outcome">The bounded outcome.</param>
    [JSImport("globalThis.openKustoExplorerCompletePerformanceOperation")]
    internal static partial void CompletePerformanceOperation(string operationId, string outcome);

    /// <summary>
    /// Completes one correlated Browser performance operation after two animation frames.
    /// </summary>
    /// <param name="operationId">The invariant operation identifier.</param>
    /// <param name="outcome">The bounded outcome.</param>
    /// <returns>A task that completes after the operation is recorded.</returns>
    [JSImport("globalThis.openKustoExplorerCompletePerformanceOperationAfterRender")]
    internal static partial Task CompletePerformanceOperationAfterRenderAsync(
        string operationId,
        string outcome);

    /// <summary>
    /// Gets whether detailed Browser profiling was requested.
    /// </summary>
    /// <returns><see langword="true"/> when the page URL contains the profile option.</returns>
    [JSImport("globalThis.openKustoExplorerIsProfilingEnabled")]
    internal static partial bool IsProfilingEnabled();

    /// <summary>
    /// Gets whether the loopback performance fixture was explicitly requested.
    /// </summary>
    /// <returns><see langword="true"/> only for an opted-in fixture page.</returns>
    [JSImport("globalThis.openKustoExplorerIsPerformanceFixtureEnabled")]
    internal static partial bool IsPerformanceFixtureEnabled();

    /// <summary>
    /// Opens management for one authenticated browser-storage partition.
    /// </summary>
    /// <param name="partition">The opaque authenticated storage partition.</param>
    [JSImport("globalThis.openKustoExplorerOpenStorageManager")]
    internal static partial void OpenStorageManager(string partition);

    /// <summary>
    /// Makes storage recovery available before the shared workbench starts.
    /// </summary>
    /// <param name="partition">The opaque authenticated storage partition.</param>
    [JSImport("globalThis.openKustoExplorerSetStoragePartition")]
    internal static partial void SetStoragePartition(string partition);

    /// <summary>
    /// Prompts for supported Kusto Explorer profile files without requesting protected-folder access.
    /// </summary>
    /// <returns>The selected relative paths and text contents as JSON, or an empty string when canceled.</returns>
    [JSImport("globalThis.openKustoExplorerSelectProfile")]
    internal static partial Task<string> SelectKustoExplorerProfileAsync();

    /// <summary>
    /// Presents one bounded in-app browser notification.
    /// </summary>
    /// <param name="title">The notification title.</param>
    /// <param name="message">The notification body.</param>
    [JSImport("globalThis.openKustoExplorerShowToast")]
    internal static partial void ShowToast(string title, string message);

    /// <summary>
    /// Submits a top-level form so an authentication redirect can replace the application page.
    /// </summary>
    /// <param name="action">The same-origin form action.</param>
    /// <param name="antiforgeryFieldName">The antiforgery form field name.</param>
    /// <param name="antiforgeryToken">The antiforgery request token.</param>
    [JSImport("globalThis.openKustoExplorerSubmitSignOut")]
    internal static partial void SubmitSignOut(
        string action,
        string antiforgeryFieldName,
        string antiforgeryToken);
}
