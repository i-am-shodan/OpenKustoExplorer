namespace OpenKustoExplorer.Application.Assistance;

/// <summary>
/// Identifies the credential used to authenticate with Azure OpenAI.
/// </summary>
public enum KustoAzureOpenAIAuthenticationKind
{
    /// <summary>
    /// Authenticate through Microsoft Entra ID using the default Azure credential chain.
    /// </summary>
    MicrosoftEntraId,

    /// <summary>
    /// Authenticate using an API key read from an environment variable.
    /// </summary>
    ApiKey,
}
