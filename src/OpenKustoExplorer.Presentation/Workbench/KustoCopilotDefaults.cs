using OpenKustoExplorer.Application.Assistance;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Defines the application defaults applied to new and existing Copilot scopes.
/// </summary>
public sealed class KustoCopilotDefaults
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoCopilotDefaults"/> class.
    /// </summary>
    /// <param name="shareTabContent">Whether complete query tab content is shared by default.</param>
    /// <param name="shareSchema">Whether schema and database context are shared by default.</param>
    /// <param name="shareResultData">Whether bounded result data is shared by default.</param>
    /// <param name="enableMicrosoftLearnMcp">Whether Microsoft Learn MCP is enabled by default.</param>
    /// <param name="enableAzureMcp">Whether read-only Azure MCP is enabled by default.</param>
    /// <param name="defaultModel">The model selected for Copilot scopes that do not have a per-tab override.</param>
    public KustoCopilotDefaults(
        bool shareTabContent,
        bool shareSchema,
        bool shareResultData,
        bool enableMicrosoftLearnMcp,
        bool enableAzureMcp,
        KustoCopilotModel? defaultModel = null)
    {
        ShareTabContent = shareTabContent;
        ShareSchema = shareSchema;
        ShareResultData = shareResultData;
        EnableMicrosoftLearnMcp = enableMicrosoftLearnMcp;
        EnableAzureMcp = shareResultData && enableAzureMcp;
        DefaultModel = defaultModel ?? new KustoCopilotModel("auto", "Automatic");
    }

    /// <summary>
    /// Gets the standard privacy-preserving defaults.
    /// </summary>
    public static KustoCopilotDefaults Standard { get; } = new(true, true, false, false, false);

    /// <summary>
    /// Gets a value indicating whether complete query tab content is shared by default.
    /// </summary>
    public bool ShareTabContent { get; }

    /// <summary>
    /// Gets a value indicating whether schema and database context are shared by default.
    /// </summary>
    public bool ShareSchema { get; }

    /// <summary>
    /// Gets a value indicating whether bounded result data is shared by default.
    /// </summary>
    public bool ShareResultData { get; }

    /// <summary>
    /// Gets a value indicating whether Microsoft Learn MCP is enabled by default.
    /// </summary>
    public bool EnableMicrosoftLearnMcp { get; }

    /// <summary>
    /// Gets a value indicating whether read-only Azure MCP is enabled by default.
    /// </summary>
    public bool EnableAzureMcp { get; }

    /// <summary>
    /// Gets the model selected for Copilot scopes that do not have a per-tab override.
    /// </summary>
    public KustoCopilotModel DefaultModel { get; }
}
