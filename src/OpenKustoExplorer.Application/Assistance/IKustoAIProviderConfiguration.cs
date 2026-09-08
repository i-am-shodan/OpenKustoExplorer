namespace OpenKustoExplorer.Application.Assistance;

/// <summary>
/// Supplies the current non-secret AI provider configuration.
/// </summary>
public interface IKustoAIProviderConfiguration
{
    /// <summary>Gets the selected provider.</summary>
    public KustoAIProviderKind ProviderKind { get; }

    /// <summary>Gets the Azure OpenAI resource endpoint.</summary>
    public string AzureOpenAIEndpoint { get; }

    /// <summary>Gets the Azure OpenAI deployment name.</summary>
    public string AzureOpenAIDeployment { get; }

    /// <summary>Gets the Azure OpenAI authentication kind.</summary>
    public KustoAzureOpenAIAuthenticationKind AzureOpenAIAuthenticationKind { get; }

    /// <summary>Gets the environment-variable name containing the Azure OpenAI API key.</summary>
    public string AzureOpenAIApiKeyEnvironmentVariable { get; }

    /// <summary>Gets the optional OpenAI-compatible API endpoint.</summary>
    public string OpenAIEndpoint { get; }

    /// <summary>Gets the OpenAI model name.</summary>
    public string OpenAIModel { get; }

    /// <summary>Gets the environment-variable name containing the OpenAI API key.</summary>
    public string OpenAIApiKeyEnvironmentVariable { get; }
}
