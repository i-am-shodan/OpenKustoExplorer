namespace OpenKustoExplorer.Application.Assistance;

/// <summary>
/// Uses GitHub Copilot to answer KQL questions and propose complete query documents.
/// </summary>
public interface IKustoCopilotService
{
    /// <summary>
    /// Opens the official GitHub Copilot CLI sign-in flow.
    /// </summary>
    /// <param name="cancellationToken">Cancels waiting for the sign-in process.</param>
    /// <returns>A task that completes when the sign-in process exits.</returns>
    public Task SignInAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets models available to the signed-in GitHub Copilot account.
    /// </summary>
    /// <param name="cancellationToken">Cancels model discovery.</param>
    /// <returns>The available models in display order.</returns>
    public Task<IReadOnlyList<KustoCopilotModel>> GetModelsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a user request with the active query-tab context.
    /// </summary>
    /// <param name="context">The immutable active-tab context.</param>
    /// <param name="request">The user's natural-language request.</param>
    /// <param name="options">The selected model and explicitly enabled MCP servers.</param>
    /// <param name="cancellationToken">Cancels the active Copilot turn.</param>
    /// <returns>The assistant response and optional full-query proposal.</returns>
    public Task<KustoCopilotReply> SendAsync(
        KustoCopilotContext context,
        string request,
        KustoCopilotOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the process-lifetime SDK conversation for one query tab.
    /// </summary>
    /// <param name="documentId">The stable query-tab identifier.</param>
    /// <param name="cancellationToken">Cancels waiting for the SDK session gate.</param>
    /// <returns>A task that completes after the tab session is disposed.</returns>
    public Task ResetConversationAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);
}
