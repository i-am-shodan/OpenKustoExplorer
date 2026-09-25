using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenKustoExplorer.Application.Assistance;
using static OpenKustoExplorer.Kusto.Gateway.V1.KustoGatewayContracts;

namespace OpenKustoExplorer.Web.Assistance;

/// <summary>
/// Executes stateless Azure OpenAI turns with managed identity credentials.
/// </summary>
internal sealed class AzureOpenAIWebCopilotService : IWebCopilotService, IDisposable
{
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromMinutes(2);
    private readonly Lock clientGate = new();
    private readonly WebCopilotOptions options;
    private IChatClient? client;
    private bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureOpenAIWebCopilotService"/> class.
    /// </summary>
    /// <param name="options">The configured Azure OpenAI settings.</param>
    public AzureOpenAIWebCopilotService(IOptions<WebCopilotOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options.Value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureOpenAIWebCopilotService"/> class with an injected client.
    /// </summary>
    /// <param name="options">The configured Azure OpenAI settings.</param>
    /// <param name="client">The provider-neutral chat client.</param>
    internal AzureOpenAIWebCopilotService(WebCopilotOptions options, IChatClient client)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(client);
        this.options = options;
        this.client = client;
    }

    /// <inheritdoc />
    public KustoCopilotModel GetModel()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ValidateConfiguration();
        return new KustoCopilotModel(options.Deployment, options.Deployment);
    }

    /// <inheritdoc />
    public async Task<KustoCopilotReply> SendAsync(
        KustoCopilotContext context,
        string request,
        string modelId,
        IReadOnlyList<KustoGatewayCopilotTurn> history,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentNullException.ThrowIfNull(history);
        ObjectDisposedException.ThrowIf(isDisposed, this);
        KustoCopilotModel model = GetModel();
        if (!string.Equals(model.Id, modelId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The selected Copilot model is not configured by this Web host.");
        }

        List<ChatMessage> messages =
        [
            new ChatMessage(ChatRole.System, KustoAssistantProtocol.SystemInstructions),
        ];
        foreach (KustoGatewayCopilotTurn turn in history)
        {
            messages.Add(new ChatMessage(ChatRole.User, turn.UserPrompt));
            messages.Add(new ChatMessage(ChatRole.Assistant, turn.AssistantResponse));
        }

        messages.Add(new ChatMessage(
            ChatRole.User,
            KustoAssistantProtocol.CreatePrompt(context, request)));

        try
        {
            using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeoutSource.CancelAfter(ResponseTimeout);
            ChatResponse response = await GetOrCreateClient().GetResponseAsync(
                messages,
                new ChatOptions { MaxOutputTokens = options.MaximumOutputTokens },
                timeoutSource.Token).ConfigureAwait(false);
            return KustoAssistantProtocol.ParseResponse(response.Text);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not WebCopilotUnavailableException)
        {
            throw new WebCopilotUnavailableException(
                "Azure OpenAI could not complete the request.",
                exception);
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
        }
    }

    private IChatClient CreateClient()
    {
        Uri endpoint = ValidateConfiguration();
        DefaultAzureCredentialOptions credentialOptions = new();
        if (!string.IsNullOrWhiteSpace(options.ManagedIdentityClientId))
        {
            credentialOptions.ManagedIdentityClientId = options.ManagedIdentityClientId.Trim();
        }

        AzureOpenAIClient azureClient = new(endpoint, new DefaultAzureCredential(credentialOptions));
        return new ChatClientBuilder(azureClient.GetChatClient(options.Deployment).AsIChatClient()).Build();
    }

    private IChatClient GetOrCreateClient()
    {
        if (client is not null)
        {
            return client;
        }

        lock (clientGate)
        {
            return client ??= CreateClient();
        }
    }

    private Uri ValidateConfiguration()
    {
        if (!Uri.TryCreate(options.AzureOpenAIEndpoint, UriKind.Absolute, out Uri? endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new WebCopilotUnavailableException(
                "Configure Copilot:AzureOpenAIEndpoint as an absolute HTTPS Azure OpenAI endpoint.");
        }

        if (string.IsNullOrWhiteSpace(options.Deployment))
        {
            throw new WebCopilotUnavailableException("Configure Copilot:Deployment for the Web host.");
        }

        if (options.MaximumOutputTokens is < 1 or > 16_384)
        {
            throw new WebCopilotUnavailableException(
                "Copilot:MaximumOutputTokens must be between 1 and 16,384.");
        }

        return endpoint;
    }
}
