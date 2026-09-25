using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Browser;
using OpenKustoExplorer.Browser.Storage;
using OpenKustoExplorer.Kusto.Gateway.V1;
using OpenKustoExplorer.Portable.Graphs;
using OpenKustoExplorer.Portable.Sessions;
using static OpenKustoExplorer.Kusto.Gateway.V1.KustoGatewayContracts;

[assembly: SupportedOSPlatform("browser")]

namespace OpenKustoExplorer.Browser;

/// <summary>
/// Starts the Avalonia browser workbench.
/// </summary>
internal static partial class Program
{
    /// <summary>
    /// Gets the same-origin Web application base URI.
    /// </summary>
    internal static Uri ApplicationBaseUri { get; private set; } = null!;

    /// <summary>
    /// Gets authenticated services initialized before Avalonia starts.
    /// </summary>
    internal static BrowserBootstrapContext BootstrapContext { get; private set; } = null!;

    /// <summary>
    /// Gets the optional loopback-only synthetic performance fixture.
    /// </summary>
    internal static BrowserPerformanceFixture? PerformanceFixture { get; private set; }

    private static async Task Main(string[] arguments)
    {
        BrowserInterop.MarkPerformance("managed.main.start");
        if (arguments.Length == 0
            || !Uri.TryCreate(arguments[0], UriKind.Absolute, out Uri? pageUri))
        {
            throw new InvalidOperationException("The browser host did not supply its page URI.");
        }

        ApplicationBaseUri = new Uri(pageUri.GetLeftPart(UriPartial.Authority), UriKind.Absolute);
        if (pageUri.IsLoopback && BrowserInterop.IsPerformanceFixtureEnabled())
        {
            PerformanceFixture = new BrowserPerformanceFixture();
        }

        HttpClient gatewayHttpClient = new()
        {
            BaseAddress = ApplicationBaseUri,
        };
        KustoGatewayClient gatewayClient = new(gatewayHttpClient);
        KustoGatewaySessionResponse session = await gatewayClient.GetSessionAsync().ConfigureAwait(false);
        BrowserInterop.SetStoragePartition(session.StoragePartition);
        BrowserInterop.MarkPerformance("managed.session.loaded");
        Task<BrowserWorkspaceStores> storesTask = LoadAndMarkAsync(
            BrowserWorkspaceStores.CreateAsync(session.StoragePartition),
            "managed.workspace.loaded");
        Task<JsonGraphStore> graphStoreTask = LoadAndMarkAsync(
            PerformanceFixture is null
                ? JsonGraphStore.CreateAsync(
                    new BrowserGraphSnapshotStore(session.StoragePartition),
                    invalidSnapshotHandler: (json, cancellationToken) => PreserveInvalidSnapshotAsync(
                        session.StoragePartition,
                        "graphs",
                        "Investigation graph",
                        json,
                        cancellationToken))
                : BrowserPerformanceFixture.CreateGraphStoreAsync(),
            "managed.graph.loaded");
        Task<JsonKustoRecordedSessionStore> recordedSessionStoreTask = LoadAndMarkAsync(
            PerformanceFixture is null
                ? JsonKustoRecordedSessionStore.CreateAsync(
                    new BrowserRecordedSessionSnapshotStore(session.StoragePartition),
                    invalidSnapshotHandler: (json, cancellationToken) => PreserveInvalidSnapshotAsync(
                        session.StoragePartition,
                        "recorded-sessions",
                        "Recorded session",
                        json,
                        cancellationToken))
                : BrowserPerformanceFixture.CreateRecordedSessionStoreAsync(),
            "managed.sessions.loaded");
        await Task.WhenAll(storesTask, graphStoreTask, recordedSessionStoreTask).ConfigureAwait(false);
        BrowserWorkspaceStores stores = await storesTask.ConfigureAwait(false);
        JsonGraphStore graphStore = await graphStoreTask.ConfigureAwait(false);
        JsonKustoRecordedSessionStore recordedSessionStore = await recordedSessionStoreTask.ConfigureAwait(false);
        BrowserAppearanceSettingsStore appearanceSettingsStore = new(session.StoragePartition);
        BootstrapContext = new BrowserBootstrapContext(
            gatewayClient,
            session,
            appearanceSettingsStore,
            graphStore,
            recordedSessionStore,
            stores);
        BrowserInterop.MarkPerformance("managed.bootstrap.complete");
        await BuildAvaloniaApp().StartBrowserAppAsync("out").ConfigureAwait(false);
    }

    private static async Task<T> LoadAndMarkAsync<T>(Task<T> task, string milestone)
    {
        T result = await task.ConfigureAwait(false);
        BrowserInterop.MarkPerformance(milestone);
        return result;
    }

    private static async Task PreserveInvalidSnapshotAsync(
        string partition,
        string name,
        string displayName,
        string json,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await BrowserStorageInterop.PreserveInvalidAsync(partition, name, json).ConfigureAwait(false);
        BrowserInterop.ShowToast(
            "Browser storage recovered",
            $"{displayName} data was archived and reset because it could not be read.");
    }

    private static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .WithInterFont();
}
