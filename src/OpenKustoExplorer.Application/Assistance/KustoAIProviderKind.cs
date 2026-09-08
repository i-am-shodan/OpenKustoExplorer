namespace OpenKustoExplorer.Application.Assistance;

/// <summary>
/// Identifies the configured language-model backend.
/// </summary>
public enum KustoAIProviderKind
{
    /// <summary>
    /// GitHub Copilot through the official Copilot SDK and CLI.
    /// </summary>
    GitHubCopilot,

    /// <summary>
    /// Azure OpenAI through the official Azure SDK.
    /// </summary>
    AzureOpenAI,

    /// <summary>
    /// OpenAI through the official OpenAI SDK.
    /// </summary>
    OpenAI,
}
