namespace OpenKustoExplorer.Application.Assistance;

/// <summary>
/// Identifies the workbench activity that owns one Copilot conversation.
/// </summary>
public enum KustoCopilotScopeKind
{
    /// <summary>
    /// The conversation belongs to one KQL document tab.
    /// </summary>
    Query,

    /// <summary>
    /// The conversation belongs to one scheduled automation.
    /// </summary>
    Automation,

    /// <summary>
    /// The conversation belongs to one recorded query session.
    /// </summary>
    RecordedSession,

    /// <summary>
    /// The conversation belongs to one named graph generation.
    /// </summary>
    Graph,
}
