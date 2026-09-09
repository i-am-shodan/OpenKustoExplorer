namespace OpenKustoExplorer.Application.Sessions;

/// <summary>
/// Identifies the scope of a user mark.
/// </summary>
public enum KustoRecordedMarkKind
{
    /// <summary>One result cell is pertinent.</summary>
    Cell,

    /// <summary>A legacy complete-row mark retained for database compatibility.</summary>
    Row,
}
