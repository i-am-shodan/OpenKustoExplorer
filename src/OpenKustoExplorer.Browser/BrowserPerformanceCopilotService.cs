using OpenKustoExplorer.Application.Assistance;

namespace OpenKustoExplorer.Browser;

/// <summary>
/// Supplies deterministic Copilot responses for loopback Browser performance runs.
/// </summary>
internal sealed class BrowserPerformanceCopilotService : IKustoCopilotService
{
    private const string ProposedQuery = """
        OutboundBrowsing
        | summarize dcount(src_ip)
            by method,
               bin(todatetime(timestamp), 1d)
        """;

    /// <inheritdoc />
    public KustoAIProviderKind ProviderKind => KustoAIProviderKind.AzureOpenAI;

    /// <inheritdoc />
    public string ProviderDisplayName => "Copilot";

    /// <inheritdoc />
    public bool SupportsInteractiveSignIn => false;

    /// <inheritdoc />
    public bool SupportsMcp => false;

    /// <inheritdoc />
    public Task SignInAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<KustoCopilotModel>> GetModelsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<KustoCopilotModel>>(
            [new KustoCopilotModel("gpt-5", "GPT-5")]);
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
        await Task.Delay(TimeSpan.FromMilliseconds(900), cancellationToken).ConfigureAwait(false);
        if (context.ScopeKind == KustoCopilotScopeKind.Graph)
        {
            return new KustoCopilotReply(
                "Alice Chen connects to the suspicious update-check domain through Workstation 042 and 198.51.100.42. That three-hop path is the clearest lead to investigate next.",
                null);
        }

        return new KustoCopilotReply(
            "The timestamp column is stored as text. Converting it to datetime before binning fixes the query; the corrected KQL is ready to apply and run.",
            ProposedQuery);
    }

    /// <inheritdoc />
    public Task ResetConversationAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
