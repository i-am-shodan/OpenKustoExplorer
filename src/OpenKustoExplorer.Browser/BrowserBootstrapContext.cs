using OpenKustoExplorer.Application.Sessions;
using OpenKustoExplorer.Browser.Storage;
using OpenKustoExplorer.Desktop.Appearance;
using OpenKustoExplorer.Graph;
using OpenKustoExplorer.Kusto.Gateway.V1;
using static OpenKustoExplorer.Kusto.Gateway.V1.KustoGatewayContracts;

namespace OpenKustoExplorer.Browser;

/// <summary>
/// Contains authenticated services and durable stores initialized before Avalonia starts.
/// </summary>
internal sealed class BrowserBootstrapContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BrowserBootstrapContext"/> class.
    /// </summary>
    /// <param name="gatewayClient">The same-origin Kusto gateway client.</param>
    /// <param name="session">The authenticated gateway session.</param>
    /// <param name="appearanceSettingsStore">The partitioned appearance-settings store.</param>
    /// <param name="graphStore">The durable browser-local graph store.</param>
    /// <param name="recordedSessionStore">The durable recorded-session store.</param>
    /// <param name="stores">The preloaded browser-local stores.</param>
    internal BrowserBootstrapContext(
        KustoGatewayClient gatewayClient,
        KustoGatewaySessionResponse session,
        IAppearanceSettingsStore appearanceSettingsStore,
        IGraphStore graphStore,
        IKustoRecordedSessionArchiveStore recordedSessionStore,
        BrowserWorkspaceStores stores)
    {
        ArgumentNullException.ThrowIfNull(gatewayClient);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(appearanceSettingsStore);
        ArgumentNullException.ThrowIfNull(graphStore);
        ArgumentNullException.ThrowIfNull(recordedSessionStore);
        ArgumentNullException.ThrowIfNull(stores);
        AppearanceSettingsStore = appearanceSettingsStore;
        GatewayClient = gatewayClient;
        GraphStore = graphStore;
        RecordedSessionStore = recordedSessionStore;
        Session = session;
        Stores = stores;
    }

    /// <summary>
    /// Gets the same-origin gateway client.
    /// </summary>
    internal KustoGatewayClient GatewayClient { get; }

    /// <summary>
    /// Gets the durable browser-local graph store.
    /// </summary>
    internal IGraphStore GraphStore { get; }

    /// <summary>
    /// Gets the durable recorded-session store.
    /// </summary>
    internal IKustoRecordedSessionArchiveStore RecordedSessionStore { get; }

    /// <summary>
    /// Gets the partitioned appearance-settings store.
    /// </summary>
    internal IAppearanceSettingsStore AppearanceSettingsStore { get; }

    /// <summary>
    /// Gets the authenticated gateway session.
    /// </summary>
    internal KustoGatewaySessionResponse Session { get; }

    /// <summary>
    /// Gets the preloaded browser-local stores.
    /// </summary>
    internal BrowserWorkspaceStores Stores { get; }
}
