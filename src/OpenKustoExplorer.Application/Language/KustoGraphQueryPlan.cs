namespace OpenKustoExplorer.Application.Language;

/// <summary>
/// Describes a caret-selected terminal graph and the validated KQL used to export its nodes and edges.
/// </summary>
public sealed class KustoGraphQueryPlan
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoGraphQueryPlan"/> class.
    /// </summary>
    /// <param name="selection">The original selected query block retained as provenance.</param>
    /// <param name="sourceKind">The terminal graph expression kind.</param>
    /// <param name="exportQueryText">The validated node-and-edge export KQL.</param>
    /// <param name="nodeTableName">The generated node result-table name.</param>
    /// <param name="edgeTableName">The generated edge result-table name.</param>
    /// <param name="nodeHashColumnName">The generated node hash column.</param>
    /// <param name="sourceHashColumnName">The generated edge source hash column.</param>
    /// <param name="targetHashColumnName">The generated edge target hash column.</param>
    /// <param name="sourceColumnName">The make-graph source property column, if available.</param>
    /// <param name="targetColumnName">The make-graph target property column, if available.</param>
    public KustoGraphQueryPlan(
        KustoQuerySelection selection,
        KustoGraphSourceKind sourceKind,
        string exportQueryText,
        string nodeTableName,
        string edgeTableName,
        string nodeHashColumnName,
        string sourceHashColumnName,
        string targetHashColumnName,
        string? sourceColumnName,
        string? targetColumnName)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentException.ThrowIfNullOrWhiteSpace(exportQueryText);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeTableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(edgeTableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeHashColumnName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceHashColumnName);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetHashColumnName);

        Selection = selection;
        SourceKind = sourceKind;
        ExportQueryText = exportQueryText;
        NodeTableName = nodeTableName;
        EdgeTableName = edgeTableName;
        NodeHashColumnName = nodeHashColumnName;
        SourceHashColumnName = sourceHashColumnName;
        TargetHashColumnName = targetHashColumnName;
        SourceColumnName = string.IsNullOrWhiteSpace(sourceColumnName) ? null : sourceColumnName.Trim();
        TargetColumnName = string.IsNullOrWhiteSpace(targetColumnName) ? null : targetColumnName.Trim();
    }

    /// <summary>
    /// Gets the original selected query block retained as provenance.
    /// </summary>
    public KustoQuerySelection Selection { get; }

    /// <summary>
    /// Gets the terminal graph expression kind.
    /// </summary>
    public KustoGraphSourceKind SourceKind { get; }

    /// <summary>
    /// Gets the validated node-and-edge export KQL.
    /// </summary>
    public string ExportQueryText { get; }

    /// <summary>
    /// Gets the generated node result-table name.
    /// </summary>
    public string NodeTableName { get; }

    /// <summary>
    /// Gets the generated edge result-table name.
    /// </summary>
    public string EdgeTableName { get; }

    /// <summary>
    /// Gets the generated node hash column.
    /// </summary>
    public string NodeHashColumnName { get; }

    /// <summary>
    /// Gets the generated edge source hash column.
    /// </summary>
    public string SourceHashColumnName { get; }

    /// <summary>
    /// Gets the generated edge target hash column.
    /// </summary>
    public string TargetHashColumnName { get; }

    /// <summary>
    /// Gets the make-graph source property column, if available.
    /// </summary>
    public string? SourceColumnName { get; }

    /// <summary>
    /// Gets the make-graph target property column, if available.
    /// </summary>
    public string? TargetColumnName { get; }
}
