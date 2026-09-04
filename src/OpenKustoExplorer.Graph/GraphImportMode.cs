namespace OpenKustoExplorer.Graph;

/// <summary>
/// Identifies how an incoming graph batch affects the active graph generation.
/// </summary>
public enum GraphImportMode
{
    /// <summary>
    /// Merges the incoming observations into the active graph generation.
    /// </summary>
    Add,

    /// <summary>
    /// Creates and atomically activates a new graph generation containing the incoming observations.
    /// </summary>
    Replace,
}
