using System.Text;
using System.Text.Json;
using OpenKustoExplorer.Application.Automations;
using OpenKustoExplorer.Application.Connections;
using OpenKustoExplorer.Application.Dashboards;
using OpenKustoExplorer.Application.Documents;
using OpenKustoExplorer.Portable.Storage;

namespace OpenKustoExplorer.Browser.Storage;

/// <summary>
/// Owns the browser-local catalogs loaded before the shared workbench is constructed.
/// </summary>
internal sealed class BrowserWorkspaceStores
{
    private BrowserWorkspaceStores(
        IKustoConnectionStore connections,
        IKustoDocumentStore documents,
        IKustoDashboardStore dashboards,
        IKustoAutomationStore automations)
    {
        Connections = connections;
        Documents = documents;
        Dashboards = dashboards;
        Automations = automations;
    }

    /// <summary>
    /// Gets the browser-local automation store.
    /// </summary>
    internal IKustoAutomationStore Automations { get; }

    /// <summary>
    /// Gets the browser-local connection store.
    /// </summary>
    internal IKustoConnectionStore Connections { get; }

    /// <summary>
    /// Gets the browser-local dashboard store.
    /// </summary>
    internal IKustoDashboardStore Dashboards { get; }

    /// <summary>
    /// Gets the browser-local document store.
    /// </summary>
    internal IKustoDocumentStore Documents { get; }

    /// <summary>
    /// Opens browser storage and loads every workbench catalog.
    /// </summary>
    /// <param name="partition">The authenticated storage partition.</param>
    /// <returns>The initialized stores.</returns>
    internal static async Task<BrowserWorkspaceStores> CreateAsync(string partition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(partition);
        await BrowserStorageInterop.OpenAsync().ConfigureAwait(false);

        Task<BrowserJsonStore<KustoConnectionCatalog>> connectionsTask = BrowserJsonStore<KustoConnectionCatalog>
            .LoadAsync(
                partition,
                "connections",
                static () => new KustoConnectionCatalog([]),
                KustoConnectionCatalogJson.Read,
                KustoConnectionCatalogJson.Write);
        Task<BrowserJsonStore<KustoDocumentWorkspace>> documentsTask = BrowserJsonStore<KustoDocumentWorkspace>
            .LoadAsync(
                partition,
                "documents",
                static () => new KustoDocumentWorkspace([], null),
                KustoDocumentWorkspaceJson.Read,
                KustoDocumentWorkspaceJson.Write);
        Task<BrowserJsonStore<KustoDashboardCatalog>> dashboardsTask = BrowserJsonStore<KustoDashboardCatalog>
            .LoadAsync(
                partition,
                "dashboards",
                static () => new KustoDashboardCatalog([]),
                KustoDashboardCatalogJson.Read,
                KustoDashboardCatalogJson.Write);
        Task<BrowserJsonStore<KustoAutomationCatalog>> automationsTask = BrowserJsonStore<KustoAutomationCatalog>
            .LoadAsync(
                partition,
                "automations",
                static () => new KustoAutomationCatalog([]),
                KustoAutomationCatalogJson.Read,
                KustoAutomationCatalogJson.Write);

        await Task.WhenAll(connectionsTask, documentsTask, dashboardsTask, automationsTask)
            .ConfigureAwait(false);
        return new BrowserWorkspaceStores(
            new BrowserConnectionStore(await connectionsTask.ConfigureAwait(false)),
            new BrowserDocumentStore(await documentsTask.ConfigureAwait(false)),
            new BrowserDashboardStore(await dashboardsTask.ConfigureAwait(false)),
            new BrowserAutomationStore(await automationsTask.ConfigureAwait(false)));
    }

    private sealed class BrowserAutomationStore : IKustoAutomationStore
    {
        private readonly BrowserJsonStore<KustoAutomationCatalog> store;

        internal BrowserAutomationStore(BrowserJsonStore<KustoAutomationCatalog> store)
        {
            this.store = store;
        }

        public KustoAutomationCatalog Load() => store.Load();

        public Task SaveAsync(
            KustoAutomationCatalog catalog,
            CancellationToken cancellationToken = default) => store.SaveAsync(catalog, cancellationToken);
    }

    private sealed class BrowserConnectionStore : IKustoConnectionStore
    {
        private readonly BrowserJsonStore<KustoConnectionCatalog> store;

        internal BrowserConnectionStore(BrowserJsonStore<KustoConnectionCatalog> store)
        {
            this.store = store;
        }

        public KustoConnectionCatalog Load() => store.Load();

        public Task SaveAsync(
            KustoConnectionCatalog catalog,
            CancellationToken cancellationToken = default) => store.SaveAsync(catalog, cancellationToken);
    }

    private sealed class BrowserDashboardStore : IKustoDashboardStore
    {
        private readonly BrowserJsonStore<KustoDashboardCatalog> store;

        internal BrowserDashboardStore(BrowserJsonStore<KustoDashboardCatalog> store)
        {
            this.store = store;
        }

        public void Export(Stream stream, KustoDashboard dashboard)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(dashboard);
            KustoDashboardCatalogJson.Write(stream, new KustoDashboardCatalog([dashboard]));
        }

        public KustoDashboard Import(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            KustoDashboardCatalog catalog = KustoDashboardCatalogJson.Read(stream);
            return catalog.Dashboards.Count == 1
                ? catalog.Dashboards[0]
                : throw new InvalidDataException("A dashboard import must contain exactly one dashboard.");
        }

        public KustoDashboardCatalog Load() => store.Load();

        public Task SaveAsync(
            KustoDashboardCatalog catalog,
            CancellationToken cancellationToken = default) => store.SaveAsync(catalog, cancellationToken);
    }

    private sealed class BrowserDocumentStore : IKustoDocumentStore
    {
        private readonly BrowserJsonStore<KustoDocumentWorkspace> store;

        internal BrowserDocumentStore(BrowserJsonStore<KustoDocumentWorkspace> store)
        {
            this.store = store;
        }

        public KustoDocumentWorkspace Load() => store.Load();

        public Task SaveAsync(
            KustoDocumentWorkspace workspace,
            CancellationToken cancellationToken = default) => store.SaveAsync(workspace, cancellationToken);
    }

    private sealed class BrowserJsonStore<T>
        where T : class
    {
        private readonly string name;
        private readonly string partition;
        private readonly Action<Stream, T> write;
        private T snapshot;

        private BrowserJsonStore(
            string partition,
            string name,
            T snapshot,
            Action<Stream, T> write)
        {
            this.partition = partition;
            this.name = name;
            this.snapshot = snapshot;
            this.write = write;
        }

        internal static async Task<BrowserJsonStore<T>> LoadAsync(
            string partition,
            string name,
            Func<T> createDefault,
            Func<Stream, T> read,
            Action<Stream, T> write)
        {
            string json = await BrowserStorageInterop.ReadAsync(partition, name).ConfigureAwait(false);
            T loadedSnapshot;
            if (string.IsNullOrEmpty(json))
            {
                loadedSnapshot = createDefault();
            }
            else
            {
                try
                {
                    using MemoryStream stream = new(Encoding.UTF8.GetBytes(json), writable: false);
                    loadedSnapshot = read(stream);
                }
                catch (Exception exception) when (exception is
                    JsonException or InvalidDataException or ArgumentException or FormatException or OverflowException)
                {
                    await BrowserStorageInterop.PreserveInvalidAsync(partition, name, json).ConfigureAwait(false);
                    BrowserInterop.ShowToast(
                        "Browser storage recovered",
                        $"{GetDisplayName(name)} data was archived and reset because it could not be read.");
                    loadedSnapshot = createDefault();
                }
            }

            return new BrowserJsonStore<T>(partition, name, loadedSnapshot, write);
        }

        internal T Load() => snapshot;

        internal async Task SaveAsync(T value, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(value);
            cancellationToken.ThrowIfCancellationRequested();
            using MemoryStream stream = new();
            write(stream, value);
            string json = Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
            await BrowserStorageInterop.WriteAsync(partition, name, json).ConfigureAwait(false);
            snapshot = value;
        }

        private static string GetDisplayName(string name)
        {
            return name switch
            {
                "automations" => "Automation",
                "connections" => "Connection",
                "dashboards" => "Dashboard",
                "documents" => "Query tab",
                _ => "Application",
            };
        }
    }
}
