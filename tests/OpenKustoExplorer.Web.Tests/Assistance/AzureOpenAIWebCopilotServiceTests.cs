using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using OpenKustoExplorer.Application.Assistance;
using OpenKustoExplorer.Web.Assistance;
using static OpenKustoExplorer.Kusto.Gateway.V1.KustoGatewayContracts;

namespace OpenKustoExplorer.Web.Tests.Assistance;

/// <summary>
/// Verifies stateless Azure OpenAI request construction independently of network credentials.
/// </summary>
public sealed class AzureOpenAIWebCopilotServiceTests
{
    /// <summary>
    /// Verifies system instructions, prior turns, and current bounded context retain their roles and order.
    /// </summary>
    /// <returns>A task that completes after the synthetic provider responds.</returns>
    [Fact]
    public async Task SendUsesSharedProtocolAndBrowserHistory()
    {
        RecordingChatClient chatClient = new();
        using AzureOpenAIWebCopilotService service = new(CreateOptions(), chatClient);
        KustoCopilotContext context = new(
            Guid.NewGuid(),
            KustoCopilotScopeKind.Query,
            "Query 1",
            "Events | take 10",
            "cluster / database",
            "Events: Timestamp:datetime",
            "Value\n42",
            null);
        KustoGatewayCopilotTurn history = new()
        {
            UserPrompt = "Earlier prompt",
            AssistantResponse = "{\"message\":\"Earlier answer\",\"query\":null,\"cypher\":null}",
        };

        KustoCopilotReply reply = await service.SendAsync(
            context,
            "Explain this query",
            "synthetic-deployment",
            [history]);

        Assert.Equal("Synthetic response", reply.Message);
        Assert.Equal("Events | project Value", reply.ProposedQuery);
        Assert.Equal(4, chatClient.Messages.Length);
        Assert.Equal(ChatRole.System, chatClient.Messages[0].Role);
        Assert.Equal(KustoAssistantProtocol.SystemInstructions, chatClient.Messages[0].Text);
        Assert.Equal("Earlier prompt", chatClient.Messages[1].Text);
        Assert.Equal(ChatRole.Assistant, chatClient.Messages[2].Role);
        Assert.Contains("<user_request>", chatClient.Messages[3].Text, StringComparison.Ordinal);
        Assert.Contains("Value\n42", chatClient.Messages[3].Text, StringComparison.Ordinal);
        Assert.Equal(2048, chatClient.Options!.MaxOutputTokens);
    }

    /// <summary>
    /// Verifies only absolute HTTPS Azure OpenAI endpoints are accepted.
    /// </summary>
    [Fact]
    public void GetModelRejectsInsecureEndpoint()
    {
        WebCopilotOptions options = CreateOptions();
        options.AzureOpenAIEndpoint = "http://synthetic.openai.azure.example";
        using AzureOpenAIWebCopilotService service = new(options, new RecordingChatClient());

        WebCopilotUnavailableException exception = Assert.Throws<WebCopilotUnavailableException>(
            service.GetModel);

        Assert.Contains("HTTPS", exception.Message, StringComparison.Ordinal);
    }

    private static WebCopilotOptions CreateOptions()
    {
        return new WebCopilotOptions
        {
            AzureOpenAIEndpoint = "https://synthetic.openai.azure.example",
            Deployment = "synthetic-deployment",
            MaximumOutputTokens = 2048,
        };
    }

    private sealed class RecordingChatClient : IChatClient
    {
        public ChatMessage[] Messages { get; private set; } = [];

        public ChatOptions? Options { get; private set; }

        public void Dispose()
        {
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Messages = messages.ToArray();
            Options = options;
            ChatMessage response = new(
                ChatRole.Assistant,
                "{\"message\":\"Synthetic response\",\"query\":\"Events | project Value\",\"cypher\":null}");
            return Task.FromResult(new ChatResponse(response));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            _ = serviceKey;
            return serviceType.IsInstanceOfType(this) ? this : null;
        }
    }
}
