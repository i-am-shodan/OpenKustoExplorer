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
        : this(modelId, enableMicrosoftLearnMcp, enableAzureMcp, false, false)
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
        : this(modelId, enableMicrosoftLearnMcp, enableAzureMcp, shareGraphData, false)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoCopilotOptions"/> class.
    /// </summary>
    /// <param name="modelId">The selected GitHub Copilot model identifier.</param>
    /// <param name="enableMicrosoftLearnMcp">Whether Microsoft Learn MCP is enabled.</param>
    /// <param name="enableAzureMcp">Whether read-only Azure MCP access is enabled.</param>
    /// <param name="shareGraphData">Whether bounded graph tools may read the scoped graph snapshot.</param>
    /// <param name="shareRecordedSessionData">Whether bounded recorded-session tools may read data.</param>
    public KustoCopilotOptions(
        string modelId,
        bool enableMicrosoftLearnMcp,
        bool enableAzureMcp,
        bool shareGraphData,
        bool shareRecordedSessionData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ModelId = modelId.Trim();
        EnableMicrosoftLearnMcp = enableMicrosoftLearnMcp;
        EnableAzureMcp = enableAzureMcp;
        ShareGraphData = shareGraphData;
        ShareRecordedSessionData = shareRecordedSessionData;
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
    /// Gets a value indicating whether bounded recorded-session tools may read data.
    /// </summary>
    public bool ShareRecordedSessionData { get; }

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
            && ShareGraphData == other.ShareGraphData
            && ShareRecordedSessionData == other.ShareRecordedSessionData;
    }
}
