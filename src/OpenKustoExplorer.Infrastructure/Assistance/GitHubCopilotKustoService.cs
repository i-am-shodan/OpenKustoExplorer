using System.Diagnostics;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.Extensions.AI;
using OpenKustoExplorer.Application.Assistance;
using OpenKustoExplorer.Application.Sessions;
using OpenKustoExplorer.Graph;
using OpenKustoExplorer.Graph.Query;

namespace OpenKustoExplorer.Infrastructure.Assistance;

/// <summary>
/// Uses the official GitHub Copilot SDK to provide current-tab KQL assistance.
/// </summary>
public sealed class GitHubCopilotKustoService : IKustoCopilotService, IDisposable, IAsyncDisposable
{
    private const string AzureMcpServerName = "azure-mcp";
    private const string MicrosoftLearnMcpServerName = "microsoft-learn";
    private static readonly string[] AzureDataExplorerMcpTools =
    [
        "get_azure_data_explorer_kusto_details",
        "kusto_cluster_get",
        "kusto_cluster_list",
        "kusto_database_list",
        "kusto_query",
        "kusto_sample",
        "kusto_table_list",
        "kusto_table_schema",
    ];

    private static readonly string[] MicrosoftLearnMcpTools =
    [
        "microsoft_docs_search",
        "microsoft_docs_fetch",
        "microsoft_code_sample_search",
    ];

    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);
    private readonly Dictionary<Guid, CopilotConversationSession> sessions = [];
    private readonly IKustoRecordedChainQueryGenerator chainQueryGenerator;
    private readonly IKustoRecordedChainSearcher chainSearcher;
    private readonly IGraphQueryService graphQueryService;
    private readonly IGraphStore graphStore;
    private readonly IKustoRecordedRelationPlanner relationPlanner;
    private readonly IKustoRecordedSessionStore sessionStore;
    private readonly SemaphoreSlim turnGate = new(1, 1);
    private CopilotClient? client;
    private bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubCopilotKustoService"/> class.
    /// </summary>
    /// <param name="graphStore">The durable graph store exposed only through consented read-only tools.</param>
    /// <param name="graphQueryService">The bounded read-only openCypher service.</param>
    /// <param name="sessionStore">The durable recorded-session store.</param>
    /// <param name="chainSearcher">The recorded evidence chain searcher.</param>
    /// <param name="relationPlanner">The recorded relation planner.</param>
    /// <param name="chainQueryGenerator">The recorded chain-query generator.</param>
    public GitHubCopilotKustoService(
        IGraphStore graphStore,
        IGraphQueryService graphQueryService,
        IKustoRecordedSessionStore sessionStore,
        IKustoRecordedChainSearcher chainSearcher,
        IKustoRecordedRelationPlanner relationPlanner,
        IKustoRecordedChainQueryGenerator chainQueryGenerator)
    {
        ArgumentNullException.ThrowIfNull(graphStore);
        ArgumentNullException.ThrowIfNull(graphQueryService);
        ArgumentNullException.ThrowIfNull(sessionStore);
        ArgumentNullException.ThrowIfNull(chainSearcher);
        ArgumentNullException.ThrowIfNull(relationPlanner);
        ArgumentNullException.ThrowIfNull(chainQueryGenerator);
        this.graphStore = graphStore;
        this.graphQueryService = graphQueryService;
        this.sessionStore = sessionStore;
        this.chainSearcher = chainSearcher;
        this.relationPlanner = relationPlanner;
        this.chainQueryGenerator = chainQueryGenerator;
    }

    /// <inheritdoc />
    public KustoAIProviderKind ProviderKind => KustoAIProviderKind.GitHubCopilot;

    /// <inheritdoc />
    public string ProviderDisplayName => "GitHub Copilot";

    /// <inheritdoc />
    public bool SupportsInteractiveSignIn => true;

    /// <inheritdoc />
    public bool SupportsMcp => true;

    /// <inheritdoc />
    public async Task SignInAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        await turnGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await ResetClientAsync().ConfigureAwait(false);
            CopilotRuntimeCommand runtime = CopilotRuntimeCommand.Find();
            using Process process = Process.Start(runtime.CreateStartInfo("login"))
                ?? throw new InvalidOperationException("Could not start GitHub Copilot sign in.");
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException("GitHub Copilot sign in did not complete.");
            }
        }
        finally
        {
            turnGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<KustoCopilotModel>> GetModelsAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        await turnGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using CancellationTokenSource startupSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            startupSource.CancelAfter(StartupTimeout);
            CopilotClient activeClient;
            IList<ModelInfo> models;

            try
            {
                activeClient = await GetOrCreateClientAsync(startupSource.Token).ConfigureAwait(false);
                models = await activeClient.ListModelsAsync(startupSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    "GitHub Copilot did not become available within 30 seconds. Check the CLI, account, and network connection.");
            }

            KustoCopilotModel[] availableModels = models
                .Where(model => !string.IsNullOrWhiteSpace(model.Id))
                .Select(model => new KustoCopilotModel(
                    model.Id,
                    string.IsNullOrWhiteSpace(model.Name) ? model.Id : model.Name))
                .OrderBy(model => model.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return Array.AsReadOnly(availableModels);
        }
        finally
        {
            turnGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<KustoCopilotReply> SendAsync(
        KustoCopilotContext context,
        string request,
        KustoCopilotOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(request);
        ArgumentNullException.ThrowIfNull(options);
        ObjectDisposedException.ThrowIf(isDisposed, this);

        await turnGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            CopilotSession activeSession = await GetOrCreateSessionAsync(
                context,
                options,
                cancellationToken)
                .ConfigureAwait(false);
            AssistantMessageEvent? response;

            try
            {
                response = await activeSession.SendAndWaitAsync(
                    new MessageOptions { Prompt = KustoAssistantProtocol.CreatePrompt(context, request) },
                    ResponseTimeout,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await activeSession.AbortAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }

            return KustoAssistantProtocol.ParseResponse(response?.Data.Content);
        }
        finally
        {
            turnGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task ResetConversationAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(documentId, Guid.Empty);
        ObjectDisposedException.ThrowIf(isDisposed, this);
        await turnGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (sessions.Remove(documentId, out CopilotConversationSession? conversation))
            {
                await conversation.Session.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            turnGate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (!isDisposed)
        {
            isDisposed = true;
            CopilotClient? activeClient = client;
            client = null;
            sessions.Clear();

            try
            {
                if (activeClient is not null)
                {
                    await StopClientAsync(activeClient).ConfigureAwait(false);
                }
            }
            finally
            {
                turnGate.Dispose();
            }
        }
    }

#pragma warning disable GHCP001
    /// <summary>
    /// Approves only allowlisted read-only tools from explicitly enabled MCP servers.
    /// </summary>
    /// <param name="request">The SDK permission request.</param>
    /// <param name="options">The current tab's explicit capability choices.</param>
    /// <returns>A one-call approval or a rejection.</returns>
    internal static PermissionDecision GetPermissionDecision(
        PermissionRequest request,
        KustoCopilotOptions options)
    {
        return GetPermissionDecision(request, options, KustoCopilotScopeKind.Query);
    }

    /// <summary>
    /// Approves exact allowlisted read-only MCP, Graph, or Recorded Sessions tools for one activity scope.
    /// </summary>
    /// <param name="request">The SDK permission request.</param>
    /// <param name="options">The conversation's explicit capability choices.</param>
    /// <param name="scopeKind">The workbench activity that owns the conversation.</param>
    /// <returns>A one-call approval or a rejection.</returns>
    internal static PermissionDecision GetPermissionDecision(
        PermissionRequest request,
        KustoCopilotOptions options,
        KustoCopilotScopeKind scopeKind)
    {
        bool approvedMcp = request is PermissionRequestMcp { ReadOnly: true } mcpRequest
            && ((options.EnableMicrosoftLearnMcp
                    && string.Equals(
                        mcpRequest.ServerName,
                        MicrosoftLearnMcpServerName,
                        StringComparison.Ordinal)
                    && MicrosoftLearnMcpTools.Contains(
                        mcpRequest.ToolName,
                        StringComparer.Ordinal))
                || (options.EnableAzureMcp
                    && string.Equals(
                        mcpRequest.ServerName,
                        AzureMcpServerName,
                        StringComparison.Ordinal)
                    && AzureDataExplorerMcpTools.Contains(
                        mcpRequest.ToolName,
                        StringComparer.Ordinal)));
        bool approvedGraphTool = request is PermissionRequestCustomTool customToolRequest
            && scopeKind == KustoCopilotScopeKind.Graph
            && options.ShareGraphData
            && IsGraphToolName(customToolRequest.ToolName);
        bool approvedRecordedSessionTool = request is PermissionRequestCustomTool sessionToolRequest
            && scopeKind == KustoCopilotScopeKind.RecordedSession
            && options.ShareRecordedSessionData
            && IsRecordedSessionToolName(sessionToolRequest.ToolName);
        return approvedMcp || approvedGraphTool || approvedRecordedSessionTool
            ? PermissionDecision.ApproveOnce()
            : PermissionDecision.Reject("Open Kusto Explorer allows only explicitly enabled read-only tools.");
    }

    private static async Task StopClientAsync(CopilotClient activeClient)
    {
        try
        {
            await activeClient.StopAsync().WaitAsync(ShutdownTimeout).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await activeClient.ForceStopAsync().ConfigureAwait(false);
        }
    }

    private static bool IsGraphToolName(string toolName)
    {
        return string.Equals(toolName, CopilotGraphTools.GetSchemaToolName, StringComparison.Ordinal)
            || string.Equals(toolName, CopilotGraphTools.QueryToolName, StringComparison.Ordinal)
            || string.Equals(toolName, CopilotGraphTools.GetEntityDetailsToolName, StringComparison.Ordinal)
            || string.Equals(toolName, CopilotGraphTools.RouteToolName, StringComparison.Ordinal);
    }

    private static bool IsRecordedSessionToolName(string toolName)
    {
        return string.Equals(toolName, CopilotRecordedSessionTools.GetOverviewToolName, StringComparison.Ordinal)
            || string.Equals(toolName, CopilotRecordedSessionTools.GetQueryToolName, StringComparison.Ordinal)
            || string.Equals(toolName, CopilotRecordedSessionTools.SearchResultsToolName, StringComparison.Ordinal)
            || string.Equals(toolName, CopilotRecordedSessionTools.GetResultPageToolName, StringComparison.Ordinal)
            || string.Equals(toolName, CopilotRecordedSessionTools.GenerateChainQueryToolName, StringComparison.Ordinal);
    }

    private static bool IsSameRecordedSessionScope(
        KustoCopilotRecordedSessionScope? left,
        KustoCopilotRecordedSessionScope? right)
    {
        return (left is null && right is null)
            || (left is not null
                && right is not null
                && left.SessionId == right.SessionId
                && ReferenceEquals(left.DatabaseSchema, right.DatabaseSchema));
    }

    private static Dictionary<string, McpServerConfig> CreateMcpServers(KustoCopilotOptions options)
    {
        Dictionary<string, McpServerConfig> servers = new(StringComparer.Ordinal);

        if (options.EnableMicrosoftLearnMcp)
        {
            servers.Add(
                MicrosoftLearnMcpServerName,
                new McpHttpServerConfig
                {
                    Url = "https://learn.microsoft.com/api/mcp",
                    Tools = MicrosoftLearnMcpTools,
                });
        }

        if (options.EnableAzureMcp)
        {
            servers.Add(
                AzureMcpServerName,
                new McpStdioServerConfig
                {
                    Command = "npx",
                    Args = ["-y", "@azure/mcp@latest", "server", "start"],
                    Tools = AzureDataExplorerMcpTools,
                });
        }

        return servers;
    }

    private async Task<CopilotSession> GetOrCreateSessionAsync(
        KustoCopilotContext context,
        KustoCopilotOptions options,
        CancellationToken cancellationToken)
    {
        CopilotClient activeClient = await GetOrCreateClientAsync(cancellationToken).ConfigureAwait(false);
        Guid documentId = context.DocumentId;
        GraphSnapshot? graphSnapshot = context.GraphSnapshot;
        KustoCopilotRecordedSessionScope? recordedSessionScope = context.RecordedSessionScope;

        if (sessions.TryGetValue(documentId, out CopilotConversationSession? existingConversation)
            && (!existingConversation.Options.IsEquivalentTo(options)
                || existingConversation.ScopeKind != context.ScopeKind
            || existingConversation.GraphSnapshot != graphSnapshot
            || !IsSameRecordedSessionScope(existingConversation.RecordedSessionScope, recordedSessionScope)))
        {
            sessions.Remove(documentId);
            await existingConversation.Session.DisposeAsync().ConfigureAwait(false);
            existingConversation = null;
        }

        if (existingConversation is null)
        {
            Dictionary<string, McpServerConfig> mcpServers = CreateMcpServers(options);
            IReadOnlyList<AIFunction> graphTools = context.ScopeKind == KustoCopilotScopeKind.Graph
                && options.ShareGraphData
                && graphSnapshot is GraphSnapshot snapshot
                    ? CopilotGraphTools.Create(graphStore, graphQueryService, snapshot)
                    : [];
            IReadOnlyList<AIFunction> recordedSessionTools = context.ScopeKind == KustoCopilotScopeKind.RecordedSession
                && options.ShareRecordedSessionData
                && recordedSessionScope is not null
                    ? CopilotRecordedSessionTools.Create(
                        sessionStore,
                        chainSearcher,
                        relationPlanner,
                        chainQueryGenerator,
                        recordedSessionScope)
                    : [];
            AIFunction[] customTools = graphTools.Concat(recordedSessionTools).ToArray();
            List<string> availableTools = customTools.Select(tool => tool.Name).ToList();

            if (options.EnableMicrosoftLearnMcp)
            {
                availableTools.AddRange(MicrosoftLearnMcpTools);
            }

            if (options.EnableAzureMcp)
            {
                availableTools.AddRange(AzureDataExplorerMcpTools);
            }

            CopilotSession session = await activeClient.CreateSessionAsync(
                new SessionConfig
                {
                    AvailableTools = availableTools,
                    ClientName = "Open Kusto Explorer",
                    EnableConfigDiscovery = false,
                    EnableFileHooks = false,
                    EnableHostGitOperations = false,
                    EnableOnDemandInstructionDiscovery = false,
                    EnableSessionStore = false,
                    EnableSessionTelemetry = false,
                    EnableSkills = false,
                    InfiniteSessions = new InfiniteSessionConfig { Enabled = false },
                    McpServers = mcpServers,
                    Model = options.ModelId,
                    OnPermissionRequest = (request, _) => Task.FromResult(
                        GetPermissionDecision(request, options, context.ScopeKind)),
                    SkipCustomInstructions = true,
                    SystemMessage = new SystemMessageConfig
                    {
                        Content = KustoAssistantProtocol.SystemInstructions,
                        Mode = SystemMessageMode.Append,
                    },
                    Tools = customTools.Cast<AIFunctionDeclaration>().ToArray(),
                },
                cancellationToken).ConfigureAwait(false);
            existingConversation = new CopilotConversationSession(
                session,
                options,
                context.ScopeKind,
                graphSnapshot,
                recordedSessionScope);
            sessions.Add(documentId, existingConversation);
        }

        return existingConversation.Session;
    }
#pragma warning restore GHCP001

    private async Task<CopilotClient> GetOrCreateClientAsync(CancellationToken cancellationToken)
    {
        if (client is null)
        {
            CopilotRuntimeCommand runtime = CopilotRuntimeCommand.Find();
            client = new CopilotClient(new CopilotClientOptions
            {
                Connection = RuntimeConnection.ForStdio(runtime.ExecutablePath, runtime.Arguments),
                LogLevel = CopilotLogLevel.Error,
                UseLoggedInUser = true,
                WorkingDirectory = AppContext.BaseDirectory,
            });
            await client.StartAsync(cancellationToken).ConfigureAwait(false);
            GetAuthStatusResponse authStatus = await client.GetAuthStatusAsync(cancellationToken)
                .ConfigureAwait(false);

            if (!authStatus.IsAuthenticated)
            {
                await ResetClientAsync().ConfigureAwait(false);
                throw new InvalidOperationException(
                    "GitHub Copilot CLI is not signed in. Choose Sign in, then try again.");
            }
        }

        return client;
    }

    private async Task ResetClientAsync()
    {
        foreach (CopilotConversationSession conversation in sessions.Values)
        {
            await conversation.Session.DisposeAsync().ConfigureAwait(false);
        }

        sessions.Clear();

        if (client is not null)
        {
            await client.DisposeAsync().ConfigureAwait(false);
            client = null;
        }
    }

    private sealed record CopilotConversationSession(
        CopilotSession Session,
        KustoCopilotOptions Options,
        KustoCopilotScopeKind ScopeKind,
        GraphSnapshot? GraphSnapshot,
        KustoCopilotRecordedSessionScope? RecordedSessionScope);
}
