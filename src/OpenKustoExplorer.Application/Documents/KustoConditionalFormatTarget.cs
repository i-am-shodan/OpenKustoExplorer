namespace OpenKustoExplorer.Application.Documents;

/// <summary>
/// Identifies which result surface receives a conditional-format color.
/// </summary>
public enum KustoConditionalFormatTarget
{
    /// <summary>
    /// Colors only the matching cell.
    /// </summary>
    Cell,

    /// <summary>
    /// Colors the complete row containing the matching cell.
    /// </summary>
    Row,
}
