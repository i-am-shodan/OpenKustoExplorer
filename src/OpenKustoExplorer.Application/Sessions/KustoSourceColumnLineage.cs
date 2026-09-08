namespace OpenKustoExplorer.Application.Sessions;

/// <summary>
/// Maps one result column to its direct source column.
/// </summary>
public sealed class KustoSourceColumnLineage
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoSourceColumnLineage"/> class.
    /// </summary>
    /// <param name="resultColumnName">The recorded result column.</param>
    /// <param name="sourceColumnName">The source-table column.</param>
    public KustoSourceColumnLineage(string resultColumnName, string sourceColumnName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resultColumnName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceColumnName);
        ResultColumnName = resultColumnName.Trim();
        SourceColumnName = sourceColumnName.Trim();
    }

    /// <summary>Gets the recorded result column.</summary>
    public string ResultColumnName { get; }

    /// <summary>Gets the source-table column.</summary>
    public string SourceColumnName { get; }
}
