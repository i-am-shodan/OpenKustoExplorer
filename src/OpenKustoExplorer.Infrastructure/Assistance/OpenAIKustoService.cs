using System.ClientModel;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenKustoExplorer.Application.Assistance;
using OpenKustoExplorer.Application.Sessions;
using OpenKustoExplorer.Graph;
using OpenKustoExplorer.Graph.Query;
using OpenAIChatClient = OpenAI.Chat.ChatClient;

namespace OpenKustoExplorer.Infrastructure.Assistance;

/// <summary>
/// Provides Kusto assistance through an OpenAI-compatible <see cref="IChatClient"/>.
/// </summary>
internal sealed class OpenAIKustoService : IKustoCopilotService, IDisposable
{
    private const int MaximumHistoryMessageCount = 21;
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromMinutes(2);
    private readonly IKustoRecordedChainQueryGenerator chainQueryGenerator;
    private readonly IKustoRecordedChainSearcher chainSearcher;
    private readonly ProviderConfiguration configuration;
    private readonly IGraphQueryService graphQueryService;
    private readonly IGraphStore graphStore;
    private readonly IKustoRecordedRelationPlanner relationPlanner;
    private readonly Dictionary<Guid, List<ChatMessage>> sessions = [];
    private readonly IKustoRecordedSessionStore sessionStore;
    private readonly SemaphoreSlim turnGate = new(1, 1);
    private IChatClient? client;
    private bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAIKustoService"/> class.
    /// </summary>
    /// <param name="configuration">The immutable provider configuration.</param>
    /// <param name="graphStore">The graph store exposed through bounded local tools.</param>
    /// <param name="graphQueryService">The graph query service.</param>
    /// <param name="sessionStore">The recorded-session store.</param>
    /// <param name="chainSearcher">The recorded chain searcher.</param>
    /// <param name="relationPlanner">The recorded relation planner.</param>
    /// <param name="chainQueryGenerator">The recorded chain-query generator.</param>
    internal OpenAIKustoService(
        ProviderConfiguration configuration,
        IGraphStore graphStore,
        IGraphQueryService graphQueryService,
        IKustoRecordedSessionStore sessionStore,
        IKustoRecordedChainSearcher chainSearcher,
        IKustoRecordedRelationPlanner relationPlanner,
        IKustoRecordedChainQueryGenerator chainQueryGenerator)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(graphStore);
        ArgumentNullException.ThrowIfNull(graphQueryService);
        ArgumentNullException.ThrowIfNull(sessionStore);
        ArgumentNullException.ThrowIfNull(chainSearcher);
        ArgumentNullException.ThrowIfNull(relationPlanner);
        ArgumentNullException.ThrowIfNull(chainQueryGenerator);
        this.configuration = configuration;
        this.graphStore = graphStore;
        this.graphQueryService = graphQueryService;
        this.sessionStore = sessionStore;
        this.chainSearcher = chainSearcher;
        this.relationPlanner = relationPlanner;
        this.chainQueryGenerator = chainQueryGenerator;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAIKustoService"/> class with an injected chat client.
    /// </summary>
    /// <param name="configuration">The immutable provider configuration.</param>
    /// <param name="graphStore">The graph store exposed through bounded local tools.</param>
    /// <param name="graphQueryService">The graph query service.</param>
    /// <param name="sessionStore">The recorded-session store.</param>
    /// <param name="chainSearcher">The recorded chain searcher.</param>
    /// <param name="relationPlanner">The recorded relation planner.</param>
    /// <param name="chainQueryGenerator">The recorded chain-query generator.</param>
    /// <param name="chatClient">The provider-neutral chat client.</param>
    internal OpenAIKustoService(
        ProviderConfiguration configuration,
        IGraphStore graphStore,
        IGraphQueryService graphQueryService,
        IKustoRecordedSessionStore sessionStore,
        IKustoRecordedChainSearcher chainSearcher,
        IKustoRecordedRelationPlanner relationPlanner,
        IKustoRecordedChainQueryGenerator chainQueryGenerator,
        IChatClient chatClient)
        : this(
            configuration,
            graphStore,
            graphQueryService,
            sessionStore,
            chainSearcher,
            relationPlanner,
            chainQueryGenerator)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        client = new ChatClientBuilder(chatClient)
            .UseFunctionInvocation()
            .Build();
    }

    /// <inheritdoc />
    public KustoAIProviderKind ProviderKind => configuration.ProviderKind;

    /// <inheritdoc />
    public string ProviderDisplayName => configuration.ProviderDisplayName;

    /// <inheritdoc />
    public bool SupportsInteractiveSignIn => false;

    /// <inheritdoc />
    public bool SupportsMcp => false;

    /// <inheritdoc />
    public Task SignInAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException(
            $"{ProviderDisplayName} uses credentials configured in application settings and does not use interactive sign in.");
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<KustoCopilotModel>> GetModelsAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateConfiguration();
        IReadOnlyList<KustoCopilotModel> models =
        [
            new KustoCopilotModel(configuration.Model, configuration.Model),
        ];
        return Task.FromResult(models);
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
            IChatClient activeClient = GetOrCreateClient();
            List<ChatMessage> history = GetOrCreateHistory(context.DocumentId);
            int originalCount = history.Count;
            history.Add(new ChatMessage(
                ChatRole.User,
                KustoAssistantProtocol.CreatePrompt(context, request)));

            try
            {
                using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
                timeoutSource.CancelAfter(ResponseTimeout);
                ChatResponse response = await activeClient.GetResponseAsync(
                    history,
                    CreateChatOptions(context, options),
                    timeoutSource.Token).ConfigureAwait(false);
                KustoCopilotReply reply = KustoAssistantProtocol.ParseResponse(response.Text);
                history.Add(new ChatMessage(ChatRole.Assistant, response.Text));
                TrimHistory(history);
                return reply;
            }
            catch
            {
                history.RemoveRange(originalCount, history.Count - originalCount);
                throw;
            }
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
            sessions.Remove(documentId);
        }
        finally
        {
            turnGate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!isDisposed)
        {
            isDisposed = true;
            client?.Dispose();
            client = null;
            sessions.Clear();
            turnGate.Dispose();
        }
    }

    private static void TrimHistory(List<ChatMessage> history)
    {
        while (history.Count > MaximumHistoryMessageCount)
        {
            history.RemoveAt(1);
        }
    }

    private static Uri CreateAbsoluteEndpoint(string value, string settingName)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttps && endpoint.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException($"{settingName} must be an absolute HTTP or HTTPS URL.");
        }

        return endpoint;
    }

    private ChatOptions CreateChatOptions(
        KustoCopilotContext context,
        KustoCopilotOptions options)
    {
        List<AITool> tools = [];
        if (context.ScopeKind == KustoCopilotScopeKind.Graph
            && options.ShareGraphData
            && context.GraphSnapshot is GraphSnapshot snapshot)
        {
            tools.AddRange(CopilotGraphTools.Create(graphStore, graphQueryService, snapshot));
        }

        if (context.ScopeKind == KustoCopilotScopeKind.RecordedSession
            && options.ShareRecordedSessionData
            && context.RecordedSessionScope is KustoCopilotRecordedSessionScope recordedScope)
        {
            tools.AddRange(CopilotRecordedSessionTools.Create(
                sessionStore,
                chainSearcher,
                relationPlanner,
                chainQueryGenerator,
                recordedScope));
        }

        return new ChatOptions { Tools = tools };
    }

    private IChatClient CreateAzureOpenAIClient()
    {
        Uri endpoint = CreateAbsoluteEndpoint(configuration.Endpoint, "Azure OpenAI endpoint");
        AzureOpenAIClient azureClient = configuration.AzureOpenAIAuthenticationKind switch
        {
            KustoAzureOpenAIAuthenticationKind.MicrosoftEntraId =>
                new AzureOpenAIClient(endpoint, new DefaultAzureCredential()),
            KustoAzureOpenAIAuthenticationKind.ApiKey =>
                new AzureOpenAIClient(endpoint, new ApiKeyCredential(ReadRequiredApiKey("Azure OpenAI"))),
            _ => throw new InvalidOperationException("Select a supported Azure OpenAI authentication method."),
        };
        return new ChatClientBuilder(azureClient.GetChatClient(configuration.Model).AsIChatClient())
            .UseFunctionInvocation()
            .Build();
    }

    private IChatClient CreateOpenAIClient()
    {
        string apiKey = ReadRequiredApiKey("OpenAI");
        OpenAIChatClient chatClient;
        if (string.IsNullOrWhiteSpace(configuration.Endpoint))
        {
            chatClient = new OpenAIChatClient(configuration.Model, apiKey);
        }
        else
        {
            OpenAIClientOptions clientOptions = new()
            {
                Endpoint = CreateAbsoluteEndpoint(configuration.Endpoint, "OpenAI endpoint"),
            };
            chatClient = new OpenAIChatClient(
                configuration.Model,
                new ApiKeyCredential(apiKey),
                clientOptions);
        }

        return new ChatClientBuilder(chatClient.AsIChatClient())
            .UseFunctionInvocation()
            .Build();
    }

    private IChatClient GetOrCreateClient()
    {
        ValidateConfiguration();
        return client ??= ProviderKind switch
        {
            KustoAIProviderKind.AzureOpenAI => CreateAzureOpenAIClient(),
            KustoAIProviderKind.OpenAI => CreateOpenAIClient(),
            _ => throw new InvalidOperationException("An OpenAI-compatible provider is not selected."),
        };
    }

    private List<ChatMessage> GetOrCreateHistory(Guid documentId)
    {
        if (!sessions.TryGetValue(documentId, out List<ChatMessage>? history))
        {
            history =
            [
                new ChatMessage(ChatRole.System, KustoAssistantProtocol.SystemInstructions),
            ];
            sessions.Add(documentId, history);
        }

        return history;
    }

    private string? ReadApiKey()
    {
        return Environment.GetEnvironmentVariable(configuration.ApiKeyEnvironmentVariable);
    }

    private string ReadRequiredApiKey(string providerDisplayName)
    {
        string? apiKey = ReadApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"{providerDisplayName} API key environment variable "
                + $"'{configuration.ApiKeyEnvironmentVariable}' is not set.");
        }

        return apiKey;
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(configuration.Model))
        {
            throw new InvalidOperationException($"Configure a model or deployment for {ProviderDisplayName}.");
        }

        if (ProviderKind == KustoAIProviderKind.AzureOpenAI)
        {
            _ = CreateAbsoluteEndpoint(configuration.Endpoint, "Azure OpenAI endpoint");
            if (configuration.AzureOpenAIAuthenticationKind == KustoAzureOpenAIAuthenticationKind.ApiKey)
            {
                _ = ReadRequiredApiKey("Azure OpenAI");
            }
        }
        else if (ProviderKind == KustoAIProviderKind.OpenAI)
        {
            if (!string.IsNullOrWhiteSpace(configuration.Endpoint))
            {
                _ = CreateAbsoluteEndpoint(configuration.Endpoint, "OpenAI endpoint");
            }

            if (string.IsNullOrWhiteSpace(ReadApiKey()))
            {
                throw new InvalidOperationException(
                    $"OpenAI API key environment variable '{configuration.ApiKeyEnvironmentVariable}' is not set.");
            }
        }
    }

    /// <summary>
    /// Contains one immutable OpenAI-compatible provider configuration.
    /// </summary>
    internal sealed class ProviderConfiguration
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ProviderConfiguration"/> class.
        /// </summary>
        /// <param name="providerKind">The provider kind.</param>
        /// <param name="endpoint">The provider endpoint.</param>
        /// <param name="model">The model or deployment.</param>
        /// <param name="apiKeyEnvironmentVariable">The API-key environment-variable name.</param>
        /// <param name="azureOpenAIAuthenticationKind">The Azure OpenAI authentication kind.</param>
        internal ProviderConfiguration(
            KustoAIProviderKind providerKind,
            string endpoint,
            string model,
            string apiKeyEnvironmentVariable,
            KustoAzureOpenAIAuthenticationKind azureOpenAIAuthenticationKind =
                KustoAzureOpenAIAuthenticationKind.MicrosoftEntraId)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            ArgumentNullException.ThrowIfNull(model);
            ArgumentException.ThrowIfNullOrWhiteSpace(apiKeyEnvironmentVariable);
            if (providerKind is not KustoAIProviderKind.AzureOpenAI and not KustoAIProviderKind.OpenAI)
            {
                throw new ArgumentOutOfRangeException(nameof(providerKind));
            }

            if (!Enum.IsDefined(azureOpenAIAuthenticationKind))
            {
                throw new ArgumentOutOfRangeException(nameof(azureOpenAIAuthenticationKind));
            }

            ProviderKind = providerKind;
            Endpoint = endpoint.Trim();
            Model = model.Trim();
            ApiKeyEnvironmentVariable = apiKeyEnvironmentVariable.Trim();
            AzureOpenAIAuthenticationKind = azureOpenAIAuthenticationKind;
        }

        /// <summary>Gets the provider kind.</summary>
        internal KustoAIProviderKind ProviderKind { get; }

        /// <summary>Gets the provider display name.</summary>
        internal string ProviderDisplayName => ProviderKind == KustoAIProviderKind.AzureOpenAI
            ? "Azure OpenAI"
            : "OpenAI";

        /// <summary>Gets the endpoint.</summary>
        internal string Endpoint { get; }

        /// <summary>Gets the model or deployment.</summary>
        internal string Model { get; }

        /// <summary>Gets the API-key environment-variable name.</summary>
        internal string ApiKeyEnvironmentVariable { get; }

        /// <summary>Gets the Azure OpenAI authentication kind.</summary>
        internal KustoAzureOpenAIAuthenticationKind AzureOpenAIAuthenticationKind { get; }

        /// <summary>Determines whether another configuration has the same provider values.</summary>
        /// <param name="other">The other configuration.</param>
        /// <returns><see langword="true"/> when the configurations are equivalent.</returns>
        internal bool IsEquivalentTo(ProviderConfiguration other)
        {
            ArgumentNullException.ThrowIfNull(other);
            return ProviderKind == other.ProviderKind
                && string.Equals(Endpoint, other.Endpoint, StringComparison.Ordinal)
                && string.Equals(Model, other.Model, StringComparison.Ordinal)
                && AzureOpenAIAuthenticationKind == other.AzureOpenAIAuthenticationKind
                && string.Equals(
                    ApiKeyEnvironmentVariable,
                    other.ApiKeyEnvironmentVariable,
                    StringComparison.Ordinal);
        }
    }
}
