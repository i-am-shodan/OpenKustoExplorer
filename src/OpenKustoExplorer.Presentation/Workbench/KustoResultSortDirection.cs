namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Defines the local sort applied to a materialized result column.
/// </summary>
public enum KustoResultSortDirection
{
    /// <summary>
    /// Preserves server row order.
    /// </summary>
    None,

    /// <summary>
    /// Orders values from low to high.
    /// </summary>
    Ascending,

    /// <summary>
    /// Orders values from high to low.
    /// </summary>
    Descending,
}
