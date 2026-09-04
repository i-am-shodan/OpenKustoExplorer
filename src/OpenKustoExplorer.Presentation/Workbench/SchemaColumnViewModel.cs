using OpenKustoExplorer.Domain.Schema;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents one column in the active Kusto database schema tree.
/// </summary>
public sealed class SchemaColumnViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SchemaColumnViewModel"/> class.
    /// </summary>
    /// <param name="columnSchema">The immutable source column schema.</param>
    /// <exception cref="ArgumentNullException"><paramref name="columnSchema"/> is <see langword="null"/>.</exception>
    public SchemaColumnViewModel(KustoColumnSchema columnSchema)
    {
        ArgumentNullException.ThrowIfNull(columnSchema);

        Name = columnSchema.Name;
        TypeName = GetTypeName(columnSchema.Type);
    }

    /// <summary>
    /// Gets the case-sensitive column name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the canonical KQL scalar type name.
    /// </summary>
    public string TypeName { get; }

    private static string GetTypeName(KustoScalarType scalarType)
    {
        string typeName = scalarType switch
        {
            KustoScalarType.Bool => "bool",
            KustoScalarType.DateTime => "datetime",
            KustoScalarType.FixedPoint => "decimal",
            KustoScalarType.Dynamic => "dynamic",
            KustoScalarType.Identifier => "guid",
            KustoScalarType.WholeNumber => "int",
            KustoScalarType.WideInteger => "long",
            KustoScalarType.Real => "real",
            KustoScalarType.Text => "string",
            KustoScalarType.TimeSpan => "timespan",
            _ => throw new ArgumentOutOfRangeException(nameof(scalarType), scalarType, "Unsupported Kusto scalar type."),
        };

        return typeName;
    }
}
