namespace OpenKustoExplorer.Application.Sessions;

/// <summary>
/// Maps a recorded query result back to a conservative source relation.
/// </summary>
public sealed class KustoRecordedRelationDescriptor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoRecordedRelationDescriptor"/> class.
    /// </summary>
    /// <param name="sourceTableName">The bound source table name.</param>
    /// <param name="isComposable">Whether the query can participate in generated KQL.</param>
    /// <param name="columns">The result-to-source column mappings.</param>
    public KustoRecordedRelationDescriptor(
        string sourceTableName,
        bool isComposable,
        IEnumerable<KustoSourceColumnLineage> columns)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceTableName);
        ArgumentNullException.ThrowIfNull(columns);
        SourceTableName = sourceTableName.Trim();
        IsComposable = isComposable;
        Columns = Array.AsReadOnly(columns.ToArray());
    }

    /// <summary>Gets the bound source table name.</summary>
    public string SourceTableName { get; }

    /// <summary>Gets a value indicating whether this relation can participate in generated KQL.</summary>
    public bool IsComposable { get; }

    /// <summary>Gets result-to-source column mappings.</summary>
    public IReadOnlyList<KustoSourceColumnLineage> Columns { get; }
}
