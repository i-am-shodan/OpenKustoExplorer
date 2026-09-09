using OpenKustoExplorer.Application.Assistance;
using OpenKustoExplorer.Application.Sessions;
using OpenKustoExplorer.Graph;
using OpenKustoExplorer.Graph.Query;

namespace OpenKustoExplorer.Infrastructure.Assistance;

/// <summary>
/// Routes assistant operations to the currently configured provider without eager network initialization.
/// </summary>
public sealed class ConfigurableKustoCopilotService : IKustoCopilotService, IDisposable, IAsyncDisposable
{
    private readonly IKustoRecordedChainQueryGenerator chainQueryGenerator;
    private readonly IKustoRecordedChainSearcher chainSearcher;
    private readonly IKustoAIProviderConfiguration configuration;
    private readonly IGraphQueryService graphQueryService;
    private readonly IGraphStore graphStore;
    private readonly IKustoCopilotService githubCopilotService;
    private readonly SemaphoreSlim providerGate = new(1, 1);
    private readonly IKustoRecordedRelationPlanner relationPlanner;
    private readonly IKustoRecordedSessionStore sessionStore;
    private OpenAIKustoService? alternativeService;
    private OpenAIKustoService.ProviderConfiguration? alternativeConfiguration;
    private bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurableKustoCopilotService"/> class.
    /// </summary>
    /// <param name="configuration">The live non-secret provider configuration.</param>
    /// <param name="githubCopilotService">The lazy GitHub Copilot service.</param>
    /// <param name="graphStore">The graph store exposed through bounded local tools.</param>
    /// <param name="graphQueryService">The graph query service.</param>
    /// <param name="sessionStore">The recorded-session store.</param>
    /// <param name="chainSearcher">The recorded chain searcher.</param>
    /// <param name="relationPlanner">The recorded relation planner.</param>
    /// <param name="chainQueryGenerator">The recorded chain-query generator.</param>
    public ConfigurableKustoCopilotService(
        IKustoAIProviderConfiguration configuration,
        IKustoCopilotService githubCopilotService,
        IGraphStore graphStore,
        IGraphQueryService graphQueryService,
        IKustoRecordedSessionStore sessionStore,
        IKustoRecordedChainSearcher chainSearcher,
        IKustoRecordedRelationPlanner relationPlanner,
        IKustoRecordedChainQueryGenerator chainQueryGenerator)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(githubCopilotService);
        ArgumentNullException.ThrowIfNull(graphStore);
        ArgumentNullException.ThrowIfNull(graphQueryService);
        ArgumentNullException.ThrowIfNull(sessionStore);
        ArgumentNullException.ThrowIfNull(chainSearcher);
        ArgumentNullException.ThrowIfNull(relationPlanner);
        ArgumentNullException.ThrowIfNull(chainQueryGenerator);
        this.configuration = configuration;
        this.githubCopilotService = githubCopilotService;
        this.graphStore = graphStore;
        this.graphQueryService = graphQueryService;
        this.sessionStore = sessionStore;
        this.chainSearcher = chainSearcher;
        this.relationPlanner = relationPlanner;
        this.chainQueryGenerator = chainQueryGenerator;
    }

    /// <inheritdoc />
    public KustoAIProviderKind ProviderKind => configuration.ProviderKind;

    /// <inheritdoc />
    public string ProviderDisplayName => ProviderKind switch
    {
        KustoAIProviderKind.GitHubCopilot => "GitHub Copilot",
        KustoAIProviderKind.AzureOpenAI => "Azure OpenAI",
        KustoAIProviderKind.OpenAI => "OpenAI",
        _ => "AI provider",
    };

    /// <inheritdoc />
    public bool SupportsInteractiveSignIn => ProviderKind == KustoAIProviderKind.GitHubCopilot;

    /// <inheritdoc />
    public bool SupportsMcp => ProviderKind == KustoAIProviderKind.GitHubCopilot;

    /// <inheritdoc />
    public Task SignInAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            service => service.SignInAsync(cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<KustoCopilotModel>> GetModelsAsync(
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            service => service.GetModelsAsync(cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<KustoCopilotReply> SendAsync(
        KustoCopilotContext context,
        string request,
        KustoCopilotOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(request);
        ArgumentNullException.ThrowIfNull(options);
        return ExecuteAsync(
            service => service.SendAsync(context, request, options, cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task ResetConversationAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(documentId, Guid.Empty);
        ObjectDisposedException.ThrowIf(isDisposed, this);
        await providerGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await githubCopilotService.ResetConversationAsync(
                documentId,
                cancellationToken).ConfigureAwait(false);
            if (alternativeService is not null)
            {
                await alternativeService.ResetConversationAsync(
                    documentId,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            providerGate.Release();
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
        if (isDisposed)
        {
            return;
        }

        await providerGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!isDisposed)
            {
                isDisposed = true;
                alternativeService?.Dispose();
                alternativeService = null;
                alternativeConfiguration = null;
            }
        }
        finally
        {
            providerGate.Release();
            providerGate.Dispose();
        }
    }

    private static OpenAIKustoService.ProviderConfiguration CreateAlternativeConfiguration(
        IKustoAIProviderConfiguration source)
    {
        return source.ProviderKind switch
        {
            KustoAIProviderKind.AzureOpenAI => new OpenAIKustoService.ProviderConfiguration(
                source.ProviderKind,
                source.AzureOpenAIEndpoint,
                source.AzureOpenAIDeployment,
                source.AzureOpenAIApiKeyEnvironmentVariable,
                source.AzureOpenAIAuthenticationKind),
            KustoAIProviderKind.OpenAI => new OpenAIKustoService.ProviderConfiguration(
                source.ProviderKind,
                source.OpenAIEndpoint,
                source.OpenAIModel,
                source.OpenAIApiKeyEnvironmentVariable),
            _ => throw new InvalidOperationException("An alternative AI provider is not selected."),
        };
    }

    private async Task ExecuteAsync(
        Func<IKustoCopilotService, Task> operation,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        await providerGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await operation(GetActiveService()).ConfigureAwait(false);
        }
        finally
        {
            providerGate.Release();
        }
    }

    private async Task<TResult> ExecuteAsync<TResult>(
        Func<IKustoCopilotService, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        await providerGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await operation(GetActiveService()).ConfigureAwait(false);
        }
        finally
        {
            providerGate.Release();
        }
    }

    private IKustoCopilotService GetActiveService()
    {
        if (ProviderKind == KustoAIProviderKind.GitHubCopilot)
        {
            DisposeAlternativeService();
            return githubCopilotService;
        }

        OpenAIKustoService.ProviderConfiguration current = CreateAlternativeConfiguration(configuration);
        if (alternativeService is null
            || alternativeConfiguration is null
            || !alternativeConfiguration.IsEquivalentTo(current))
        {
            DisposeAlternativeService();
            alternativeConfiguration = current;
            alternativeService = new OpenAIKustoService(
                current,
                graphStore,
                graphQueryService,
                sessionStore,
                chainSearcher,
                relationPlanner,
                chainQueryGenerator);
        }

        return alternativeService;
    }

    private void DisposeAlternativeService()
    {
        alternativeService?.Dispose();
        alternativeService = null;
        alternativeConfiguration = null;
    }
}
