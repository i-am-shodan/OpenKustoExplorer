using Avalonia;
using OpenKustoExplorer.Application.Assistance;
using OpenKustoExplorer.Application.Connections;
using OpenKustoExplorer.Application.Diagnostics;
using OpenKustoExplorer.Application.Documents;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Graphs;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Desktop;
using OpenKustoExplorer.Desktop.Appearance;
using OpenKustoExplorer.Domain.Schema;
using OpenKustoExplorer.Graph;
using OpenKustoExplorer.Graph.Query;
using OpenKustoExplorer.Kusto.Gateway.V1;
using OpenKustoExplorer.Kusto.Language;
using OpenKustoExplorer.Portable.Assistance;
using OpenKustoExplorer.Portable.Graphs;
using OpenKustoExplorer.Portable.Sessions;
using OpenKustoExplorer.Presentation.Workbench;

namespace OpenKustoExplorer.Browser;

/// <summary>
/// Composes the shared workbench with browser-safe services.
/// </summary>
internal static class BrowserWorkbenchFactory
{
    /// <summary>
    /// Creates the complete workbench for one browser session.
    /// </summary>
    /// <param name="application">The initialized browser application.</param>
    /// <param name="bootstrapContext">The authenticated Browser services and preloaded stores.</param>
    /// <returns>The shared workbench view.</returns>
    internal static WorkbenchView Create(
        Avalonia.Application application,
        BrowserBootstrapContext bootstrapContext)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(bootstrapContext);

        IGraphStore graphStore = bootstrapContext.GraphStore;
        IGraphQueryService graphQueryService = (IGraphQueryService)graphStore;
        KustoGatewayClient gatewayClient = bootstrapContext.GatewayClient;
        BrowserPerformanceFixture? performanceFixture = Program.PerformanceFixture;
        IKustoCatalogService catalogService = performanceFixture is null ? gatewayClient : performanceFixture;
        IKustoConnectionStore connectionStore = performanceFixture is null
            ? bootstrapContext.Stores.Connections
            : performanceFixture;
        IKustoDocumentStore documentStore = performanceFixture is null
            ? bootstrapContext.Stores.Documents
            : performanceFixture;
        IKustoQueryService queryService = performanceFixture is null ? gatewayClient : performanceFixture;
        IKustoCopilotService copilotService = performanceFixture is null
            ? new BrowserWebCopilotService(
                gatewayClient,
                new KustoCopilotSharedDataBuilder(
                    graphStore,
                    graphQueryService,
                    bootstrapContext.RecordedSessionStore))
            : new BrowserPerformanceCopilotService();
        BrowserIdentityService identityService = new(gatewayClient);
        IWorkbenchPerformanceSink performanceSink = BrowserInterop.IsProfilingEnabled()
            ? new BrowserWorkbenchPerformanceSink()
            : NullWorkbenchPerformanceSink.Instance;
        BrowserInterop.MarkPerformance("appearance.create.start");
        AppearanceSettings appearanceSettings = AppearanceSettings.CreatePersistent(
            bootstrapContext.AppearanceSettingsStore);
        appearanceSettings.Initialize(application);
        BrowserInterop.MarkPerformance("appearance.create.complete");
        BrowserInterop.MarkPerformance("viewmodel.create.start");
        MainWindowViewModel viewModel = new(
            new KustoLanguageService(),
            queryService,
            catalogService,
            connectionStore,
            documentStore,
            bootstrapContext.Stores.Dashboards,
            bootstrapContext.Stores.Automations,
            new BrowserKustoExplorerImportService(),
            copilotService,
            new KustoGraphIngestionCoordinator(
                gatewayClient,
                graphStore,
                new BufferedKustoGraphImportStagingSourceFactory()),
            graphStore,
            new MsaglGraphLayoutService(),
            new WorkbenchHostCapabilities(
                KustoExplorerImportMode.UserSelectedProfileFolder,
                supportsToastNotifications: true,
                supportsEmailNotifications: false,
                supportsApplicationLaunchNotifications: false,
                toastNotificationName: "Browser toast",
                WorkbenchAIProviderOwnership.HostManaged,
                KustoAIProviderKind.AzureOpenAI,
                managedAIProviderDisplayName: "Azure OpenAI (managed by Web host)",
                supportsMcp: false,
                WorkbenchIdentityMode.HostAuthenticatedAccount,
                WorkbenchStorageManagementMode.BrowserDialog,
                GraphStorageLimits.MaximumEntityCount,
                GraphStorageLimits.MaximumRelationshipCount,
                supportsWebhookNotifications: false),
            bootstrapContext.RecordedSessionStore,
            new KustoPredicateInterestExtractor(),
            new KustoRecordedRelationExtractor(),
            new KustoRecordedChainSearcher(bootstrapContext.RecordedSessionStore),
            new KustoRecordedRelationPlanner(),
            new KustoRecordedChainQueryGenerator(),
            timeProvider: TimeProvider.System,
            recordedSessionArchiveService: new KustoRecordedSessionArchiveService(
                bootstrapContext.RecordedSessionStore,
                TimeProvider.System),
            performanceSink: performanceSink);
        BrowserInterop.MarkPerformance("viewmodel.create.complete");

        BrowserInterop.MarkPerformance("view.create.start");
        WorkbenchView view = new(
            viewModel,
            appearanceSettings,
            identityService,
            new BrowserAutomationNotificationDispatcher(),
            performanceSink: performanceSink,
            storageManagementAction: () => BrowserInterop.OpenStorageManager(
                bootstrapContext.Session.StoragePartition));
        if (performanceFixture is not null)
        {
            BrowserPerformanceExports.Initialize(view, performanceFixture);
        }

        BrowserInterop.MarkPerformance("view.create.complete");
        return view;
    }

    private sealed class BrowserIdentityService : IKustoIdentityService
    {
        private readonly KustoGatewayClient gatewayClient;

        public BrowserIdentityService(KustoGatewayClient gatewayClient)
        {
            this.gatewayClient = gatewayClient;
        }

        public event EventHandler? SignedInUsersChanged
        {
            add => _ = value;
            remove => _ = value;
        }

        public async Task<IReadOnlyList<KustoSignedInUser>> GetSignedInUsersAsync(
            CancellationToken cancellationToken = default)
        {
            KustoGatewayContracts.KustoGatewaySessionResponse session = await gatewayClient
                .GetSessionAsync(cancellationToken)
                .ConfigureAwait(false);
            return
            [
                new KustoSignedInUser(
                    session.AccountId,
                    session.DisplayName,
                    session.AccountName,
                    []),
            ];
        }

        public async Task SignOutAsync(
            string accountId,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
            KustoGatewayContracts.KustoGatewaySessionResponse session = await gatewayClient
                .GetSessionAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(accountId, session.AccountId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The requested Web account is not signed in.");
            }

            BrowserInterop.SubmitSignOut(
                KustoGatewayRoutes.SignOut,
                KustoGatewayRoutes.AntiforgeryFormFieldName,
                session.AntiforgeryToken);
        }
    }
}
