using OpenKustoExplorer.Domain.Schema;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents one table and its columns in the active Kusto database schema tree.
/// </summary>
public sealed class SchemaTableViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SchemaTableViewModel"/> class.
    /// </summary>
    /// <param name="tableSchema">The immutable source table schema.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tableSchema"/> is <see langword="null"/>.</exception>
    public SchemaTableViewModel(KustoTableSchema tableSchema)
    {
        ArgumentNullException.ThrowIfNull(tableSchema);

        Name = tableSchema.Name;
        Columns = Array.AsReadOnly(tableSchema.Columns.Select(column => new SchemaColumnViewModel(column)).ToArray());
    }

    /// <summary>
    /// Gets the case-sensitive table name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the table columns shown in schema order.
    /// </summary>
    public IReadOnlyList<SchemaColumnViewModel> Columns { get; }
}
