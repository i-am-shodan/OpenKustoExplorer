using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;
using OpenKustoExplorer.Application.Assistance;
using OpenKustoExplorer.Application.Sessions;
using OpenKustoExplorer.Graph;
using OpenKustoExplorer.Infrastructure.Assistance;
using OpenKustoExplorer.Infrastructure.Graph;
using OpenKustoExplorer.Infrastructure.Sessions;
using OpenKustoExplorer.Portable.Sessions;

namespace OpenKustoExplorer.Infrastructure.Tests.Assistance;

/// <summary>
/// Verifies lazy, configuration-aware AI provider routing.
/// </summary>
public sealed class ConfigurableKustoCopilotServiceTests
{
    /// <summary>
    /// Verifies alternative providers share the structured protocol, conversation history, and bounded tools.
    /// </summary>
    /// <returns>A task that completes after provider-neutral turns are verified.</returns>
    [Fact]
    public async Task AlternativeProviderUsesSharedProtocolHistoryAndTools()
    {
        string directoryPath = Path.Combine(
            Path.GetTempPath(),
            $"OpenKustoExplorer-AIClient-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);

        try
        {
            using SqliteGraphStore graphStore = new(Path.Combine(directoryPath, "graph.db"));
            using SqliteKustoRecordedSessionStore sessionStore = new(
                Path.Combine(directoryPath, "sessions.db"));
            KustoRecordedChainSearcher chainSearcher = new(sessionStore);
            RecordingChatClient chatClient = new();
            using OpenAIKustoService service = new(
                new OpenAIKustoService.ProviderConfiguration(
                    KustoAIProviderKind.AzureOpenAI,
                    "https://synthetic.openai.azure.example",
                    "synthetic-deployment",
                    "SYNTHETIC_AZURE_OPENAI_KEY"),
                graphStore,
                graphStore,
                sessionStore,
                chainSearcher,
                new KustoRecordedRelationPlanner(),
                new KustoRecordedChainQueryGenerator(),
                chatClient);
            Guid conversationId = Guid.NewGuid();
            KustoCopilotContext queryContext = new(
                conversationId,
                "Synthetic query",
                "SyntheticEvents | take 10",
                "synthetic.example / Samples",
                "SyntheticEvents: Value:string",
                string.Empty);

            KustoCopilotReply firstReply = await service.SendAsync(
                queryContext,
                "Add a projection",
                new KustoCopilotOptions("synthetic-deployment", false, false));
            await service.SendAsync(
                queryContext,
                "Now summarize it",
                new KustoCopilotOptions("synthetic-deployment", false, false));

            Assert.Equal("SyntheticEvents | project Value", firstReply.ProposedQuery);
            Assert.Equal(4, chatClient.Messages.Count);
            Assert.Equal(ChatRole.System, chatClient.Messages[0].Role);
            Assert.Contains("Treat the supplied", chatClient.Messages[0].Text, StringComparison.Ordinal);
            Assert.Contains("Add a projection", chatClient.Messages[1].Text, StringComparison.Ordinal);
            Assert.Contains("SyntheticEvents | take 10", chatClient.Messages[1].Text, StringComparison.Ordinal);

            GraphStateSummary graphState = await graphStore.GetStateAsync();
            KustoCopilotContext graphContext = new(
                Guid.NewGuid(),
                KustoCopilotScopeKind.Graph,
                "Synthetic graph",
                "MATCH (n) RETURN n",
                "0 nodes, 0 edges",
                string.Empty,
                string.Empty,
                graphState.Snapshot);
            await service.SendAsync(
                graphContext,
                "Describe the graph",
                new KustoCopilotOptions("synthetic-deployment", false, false, true));
            Assert.Equal(4, chatClient.Options?.Tools?.Count);

            KustoCopilotContext sessionContext = new(
                Guid.NewGuid(),
                KustoCopilotScopeKind.RecordedSession,
                "Synthetic session",
                string.Empty,
                "synthetic.example / Samples",
                "Recorded queries: 0",
                string.Empty,
                null,
                new KustoCopilotRecordedSessionScope(Guid.NewGuid(), null));
            await service.SendAsync(
                sessionContext,
                "Find a recorded value",
                new KustoCopilotOptions("synthetic-deployment", false, false, false, true));
            Assert.Equal(5, chatClient.Options?.Tools?.Count);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, true);
            }
        }
    }

    /// <summary>
    /// Verifies provider construction is offline-safe and configuration errors surface only when invoked.
    /// </summary>
    /// <returns>A task that completes after provider routing is verified.</returns>
    [Fact]
    public async Task RouterIsLazyAndKeepsGitHubCopilotAsDefault()
    {
        string directoryPath = Path.Combine(
            Path.GetTempPath(),
            $"OpenKustoExplorer-AIRouter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        string missingKeyVariable = $"OPENKUSTOEXPLORER_MISSING_KEY_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(missingKeyVariable, null);

        try
        {
            using SqliteGraphStore graphStore = new(Path.Combine(directoryPath, "graph.db"));
            using SqliteKustoRecordedSessionStore sessionStore = new(
                Path.Combine(directoryPath, "sessions.db"));
            MutableProviderConfiguration configuration = new()
            {
                OpenAIApiKeyEnvironmentVariable = missingKeyVariable,
            };
            StubCopilotService githubService = new();
            KustoRecordedChainSearcher chainSearcher = new(sessionStore);
            await using ConfigurableKustoCopilotService router = new(
                configuration,
                githubService,
                graphStore,
                graphStore,
                sessionStore,
                chainSearcher,
                new KustoRecordedRelationPlanner(),
                new KustoRecordedChainQueryGenerator());

            Assert.Equal(KustoAIProviderKind.GitHubCopilot, router.ProviderKind);
            Assert.Equal("GitHub Copilot", router.ProviderDisplayName);
            Assert.True(router.SupportsInteractiveSignIn);
            Assert.True(router.SupportsMcp);
            Assert.Equal(0, githubService.CallCount);

            IReadOnlyList<KustoCopilotModel> githubModels = await router.GetModelsAsync();

            Assert.Single(githubModels);
            Assert.Equal(1, githubService.CallCount);

            configuration.ProviderKind = KustoAIProviderKind.OpenAI;
            Assert.False(router.SupportsInteractiveSignIn);
            Assert.False(router.SupportsMcp);
            InvalidOperationException openAIError = await Assert.ThrowsAsync<InvalidOperationException>(
                () => router.GetModelsAsync());
            Assert.Contains(missingKeyVariable, openAIError.Message, StringComparison.Ordinal);
            Assert.Equal(1, githubService.CallCount);

            configuration.ProviderKind = KustoAIProviderKind.AzureOpenAI;
            configuration.AzureOpenAIDeployment = "synthetic-deployment";
            InvalidOperationException azureError = await Assert.ThrowsAsync<InvalidOperationException>(
                () => router.GetModelsAsync());
            Assert.Contains("endpoint", azureError.Message, StringComparison.OrdinalIgnoreCase);

            configuration.AzureOpenAIEndpoint = "https://synthetic.openai.azure.example";
            IReadOnlyList<KustoCopilotModel> azureModels = await router.GetModelsAsync();
            Assert.Equal("synthetic-deployment", Assert.Single(azureModels).Id);

            configuration.AzureOpenAIAuthenticationKind = KustoAzureOpenAIAuthenticationKind.ApiKey;
            configuration.AzureOpenAIApiKeyEnvironmentVariable = missingKeyVariable;
            InvalidOperationException azureKeyError = await Assert.ThrowsAsync<InvalidOperationException>(
                () => router.GetModelsAsync());
            Assert.Contains(missingKeyVariable, azureKeyError.Message, StringComparison.Ordinal);

            configuration.AzureOpenAIAuthenticationKind = KustoAzureOpenAIAuthenticationKind.MicrosoftEntraId;
            azureModels = await router.GetModelsAsync();
            Assert.Equal("synthetic-deployment", Assert.Single(azureModels).Id);

            configuration.ProviderKind = KustoAIProviderKind.GitHubCopilot;
            await router.GetModelsAsync();
            Assert.Equal(2, githubService.CallCount);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, true);
            }
        }
    }

    private sealed class MutableProviderConfiguration : IKustoAIProviderConfiguration
    {
        public KustoAIProviderKind ProviderKind { get; set; }

        public string AzureOpenAIEndpoint { get; set; } = string.Empty;

        public string AzureOpenAIDeployment { get; set; } = string.Empty;

        public KustoAzureOpenAIAuthenticationKind AzureOpenAIAuthenticationKind { get; set; }

        public string AzureOpenAIApiKeyEnvironmentVariable { get; set; } = "AZURE_OPENAI_API_KEY";

        public string OpenAIEndpoint { get; set; } = string.Empty;

        public string OpenAIModel { get; set; } = "synthetic-model";

        public string OpenAIApiKeyEnvironmentVariable { get; set; } = "OPENAI_API_KEY";
    }

    private sealed class StubCopilotService : IKustoCopilotService
    {
        public int CallCount { get; private set; }

        public KustoAIProviderKind ProviderKind => KustoAIProviderKind.GitHubCopilot;

        public string ProviderDisplayName => "GitHub Copilot";

        public bool SupportsInteractiveSignIn => true;

        public bool SupportsMcp => true;

        public Task SignInAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<KustoCopilotModel>> GetModelsAsync(
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            IReadOnlyList<KustoCopilotModel> models =
            [
                new KustoCopilotModel("synthetic-copilot-model", "Synthetic Copilot model"),
            ];
            return Task.FromResult(models);
        }

        public Task<KustoCopilotReply> SendAsync(
            KustoCopilotContext context,
            string request,
            KustoCopilotOptions options,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new KustoCopilotReply("Synthetic response", null));
        }

        public Task ResetConversationAsync(
            Guid documentId,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingChatClient : IChatClient
    {
        public System.Collections.ObjectModel.ReadOnlyCollection<ChatMessage> Messages { get; private set; } =
            Array.AsReadOnly(Array.Empty<ChatMessage>());

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
            Messages = Array.AsReadOnly(messages.ToArray());
            Options = options;
            ChatMessage response = new(
                ChatRole.Assistant,
                "{\"message\":\"Synthetic response\",\"query\":\"SyntheticEvents | project Value\",\"cypher\":null}");
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
