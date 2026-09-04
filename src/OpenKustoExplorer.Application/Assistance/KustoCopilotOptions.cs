namespace OpenKustoExplorer.Application.Assistance;

/// <summary>
/// Contains user-selected model and MCP capabilities for one Copilot turn.
/// </summary>
public sealed class KustoCopilotOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoCopilotOptions"/> class.
    /// </summary>
    /// <param name="modelId">The selected GitHub Copilot model identifier.</param>
    /// <param name="enableMicrosoftLearnMcp">Whether Microsoft Learn MCP is enabled.</param>
    /// <param name="enableAzureMcp">Whether read-only Azure MCP access is enabled.</param>
    public KustoCopilotOptions(
        string modelId,
        bool enableMicrosoftLearnMcp,
        bool enableAzureMcp)
        : this(modelId, enableMicrosoftLearnMcp, enableAzureMcp, false)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoCopilotOptions"/> class.
    /// </summary>
    /// <param name="modelId">The selected GitHub Copilot model identifier.</param>
    /// <param name="enableMicrosoftLearnMcp">Whether Microsoft Learn MCP is enabled.</param>
    /// <param name="enableAzureMcp">Whether read-only Azure MCP access is enabled.</param>
    /// <param name="shareGraphData">Whether bounded graph tools may read the scoped graph snapshot.</param>
    public KustoCopilotOptions(
        string modelId,
        bool enableMicrosoftLearnMcp,
        bool enableAzureMcp,
        bool shareGraphData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ModelId = modelId.Trim();
        EnableMicrosoftLearnMcp = enableMicrosoftLearnMcp;
        EnableAzureMcp = enableAzureMcp;
        ShareGraphData = shareGraphData;
    }

    /// <summary>
    /// Gets the selected GitHub Copilot model identifier.
    /// </summary>
    public string ModelId { get; }

    /// <summary>
    /// Gets a value indicating whether Microsoft Learn MCP is enabled.
    /// </summary>
    public bool EnableMicrosoftLearnMcp { get; }

    /// <summary>
    /// Gets a value indicating whether read-only Azure MCP access is enabled.
    /// </summary>
    public bool EnableAzureMcp { get; }

    /// <summary>
    /// Gets a value indicating whether bounded graph tools may read the scoped graph snapshot.
    /// </summary>
    public bool ShareGraphData { get; }

    /// <summary>
    /// Determines whether another option set requires the same SDK session configuration.
    /// </summary>
    /// <param name="other">The other option set.</param>
    /// <returns><see langword="true"/> when model and MCP settings match.</returns>
    public bool IsEquivalentTo(KustoCopilotOptions other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return string.Equals(ModelId, other.ModelId, StringComparison.Ordinal)
            && EnableMicrosoftLearnMcp == other.EnableMicrosoftLearnMcp
            && EnableAzureMcp == other.EnableAzureMcp
            && ShareGraphData == other.ShareGraphData;
    }
}
