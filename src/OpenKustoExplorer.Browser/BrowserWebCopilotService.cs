using OpenKustoExplorer.Application.Assistance;
using OpenKustoExplorer.Kusto.Gateway.V1;
using OpenKustoExplorer.Portable.Assistance;
using static OpenKustoExplorer.Kusto.Gateway.V1.KustoGatewayContracts;

namespace OpenKustoExplorer.Browser;

/// <summary>
/// Keeps Copilot conversation state and consented data assembly inside the browser.
/// </summary>
internal sealed class BrowserWebCopilotService : IKustoCopilotService, IDisposable
{
    private readonly KustoCopilotSharedDataBuilder sharedDataBuilder;
    private readonly KustoGatewayClient gatewayClient;
    private readonly SemaphoreSlim historyGate = new(1, 1);
    private readonly Dictionary<Guid, List<KustoGatewayCopilotTurn>> historyByDocument = [];
    private bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="BrowserWebCopilotService"/> class.
    /// </summary>
    /// <param name="gatewayClient">The authenticated same-origin gateway client.</param>
    /// <param name="sharedDataBuilder">The browser-local bounded snapshot builder.</param>
    internal BrowserWebCopilotService(
        KustoGatewayClient gatewayClient,
        KustoCopilotSharedDataBuilder sharedDataBuilder)
    {
        ArgumentNullException.ThrowIfNull(gatewayClient);
        ArgumentNullException.ThrowIfNull(sharedDataBuilder);
        this.gatewayClient = gatewayClient;
        this.sharedDataBuilder = sharedDataBuilder;
    }

    /// <inheritdoc />
    public KustoAIProviderKind ProviderKind => KustoAIProviderKind.AzureOpenAI;

    /// <inheritdoc />
    public string ProviderDisplayName => "Azure OpenAI";

    /// <inheritdoc />
    public bool SupportsInteractiveSignIn => false;

    /// <inheritdoc />
    public bool SupportsMcp => false;

    /// <inheritdoc />
    public Task SignInAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException("Azure OpenAI uses the Web host's managed identity.");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<KustoCopilotModel>> GetModelsAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        KustoGatewayCopilotModelsResponse response = await gatewayClient
            .GetCopilotModelsAsync(cancellationToken)
            .ConfigureAwait(false);
        KustoAIProviderKind providerKind = (KustoAIProviderKind)response.ProviderKind;
        if (providerKind != KustoAIProviderKind.AzureOpenAI)
        {
            throw new InvalidDataException("The Web host returned an unsupported Copilot provider.");
        }

        return (response.Models
            ?? throw new InvalidDataException("The Web host returned no Copilot model collection."))
            .Select(model => new KustoCopilotModel(model.Id, model.DisplayName))
            .ToArray();
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
        string sharedDataText = await sharedDataBuilder.CreateAsync(
            context,
            options,
            cancellationToken).ConfigureAwait(false);
        KustoCopilotContext transmittedContext = new(
            context.DocumentId,
            context.ScopeKind,
            context.DocumentTitle,
            context.QueryText,
            context.TargetText,
            context.SchemaText,
            sharedDataText,
            null,
            null);
        await historyGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            List<KustoGatewayCopilotTurn> history = GetOrCreateHistory(context.DocumentId);
            KustoCopilotReply reply = await gatewayClient.SendCopilotAsync(
                transmittedContext,
                request,
                options.ModelId,
                sharedDataText,
                history,
                cancellationToken).ConfigureAwait(false);
            history.Add(new KustoGatewayCopilotTurn
            {
                AssistantResponse = KustoAssistantProtocol.CreateResponse(reply),
                UserPrompt = KustoAssistantProtocol.CreatePrompt(transmittedContext, request),
            });
            TrimHistory(history);
            return reply;
        }
        finally
        {
            historyGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task ResetConversationAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(documentId, Guid.Empty);
        ObjectDisposedException.ThrowIf(isDisposed, this);
        await historyGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            historyByDocument.Remove(documentId);
        }
        finally
        {
            historyGate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!isDisposed)
        {
            isDisposed = true;
            historyByDocument.Clear();
            historyGate.Dispose();
        }
    }

    private static void TrimHistory(List<KustoGatewayCopilotTurn> history)
    {
        while (history.Count > KustoGatewayRoutes.MaximumCopilotHistoryTurnCount)
        {
            history.RemoveAt(0);
        }
    }

    private List<KustoGatewayCopilotTurn> GetOrCreateHistory(Guid documentId)
    {
        if (!historyByDocument.TryGetValue(documentId, out List<KustoGatewayCopilotTurn>? history))
        {
            history = [];
            historyByDocument.Add(documentId, history);
        }

        return history;
    }
}
