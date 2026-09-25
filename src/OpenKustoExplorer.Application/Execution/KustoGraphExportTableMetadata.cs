using System.Text;
using System.Text.Json;
using OpenKustoExplorer.Application.Language;

namespace OpenKustoExplorer.Application.Execution;

/// <summary>
/// Describes one validated graph export table and its mapped control columns.
/// </summary>
public sealed class KustoGraphExportTableMetadata
{
    private static readonly string[] EntityIdColumnNames =
    [
        "id",
        "entityid",
        "objectid",
        "nodeid",
    ];

    private static readonly string[] EntityLabelColumnNames =
    [
        "displayname",
        "name",
        "label",
        "title",
    ];

    private static readonly string[] EntityTypeColumnNames =
    [
        "entitytype",
        "nodetype",
        "type",
        "kind",
        "category",
    ];

    private static readonly string[] EntityTypeFallbackColumnNames =
    [
        "label",
    ];

    private static readonly string[] RelationshipIdColumnNames =
    [
        "edgeid",
        "relationshipid",
        "id",
    ];

    private static readonly string[] RelationshipTypeColumnNames =
    [
        "relationshiptype",
        "edgetype",
        "relationship",
        "type",
        "label",
    ];

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoGraphExportTableMetadata"/> class.
    /// </summary>
    /// <param name="kind">Whether the table contains nodes or edges.</param>
    /// <param name="tableName">The generated result-table name.</param>
    /// <param name="columns">The ordered result columns.</param>
    /// <param name="plan">The validated graph export plan.</param>
    internal KustoGraphExportTableMetadata(
        KustoGraphExportTableKind kind,
        string tableName,
        IReadOnlyList<KustoResultColumn> columns,
        KustoGraphQueryPlan plan)
    {
        if (columns.Count == 0)
        {
            throw new InvalidDataException($"Graph export table '{tableName}' contains no columns.");
        }

        KustoResultColumn[] columnSnapshot = columns.ToArray();
        string? duplicateColumnName = columnSnapshot
            .GroupBy(column => column.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .FirstOrDefault();
        if (duplicateColumnName is not null)
        {
            throw new InvalidDataException(
                $"Graph export table '{tableName}' contains duplicate column '{duplicateColumnName}'.");
        }

        Kind = kind;
        TableName = tableName;
        Columns = Array.AsReadOnly(columnSnapshot);
        NodeHashIndex = kind == KustoGraphExportTableKind.Nodes
            ? FindColumnIndex(columnSnapshot, plan.NodeHashColumnName)
            : -1;
        SourceHashIndex = kind == KustoGraphExportTableKind.Edges
            ? FindColumnIndex(columnSnapshot, plan.SourceHashColumnName)
            : -1;
        TargetHashIndex = kind == KustoGraphExportTableKind.Edges
            ? FindColumnIndex(columnSnapshot, plan.TargetHashColumnName)
            : -1;
        if ((kind == KustoGraphExportTableKind.Nodes && NodeHashIndex < 0)
            || (kind == KustoGraphExportTableKind.Edges
                && (SourceHashIndex < 0 || TargetHashIndex < 0)))
        {
            throw new InvalidDataException($"Graph export table '{tableName}' is missing generated hash columns.");
        }

        HashSet<int> controlIndexes = [NodeHashIndex, SourceHashIndex, TargetHashIndex];
        controlIndexes.Remove(-1);
        EntityIdIndex = FindMappedColumnIndex(columnSnapshot, EntityIdColumnNames, controlIndexes);
        EntityLabelIndex = FindMappedColumnIndex(columnSnapshot, EntityLabelColumnNames, controlIndexes);
        EntityTypeIndex = FindMappedColumnIndex(columnSnapshot, EntityTypeColumnNames, controlIndexes);
        EntityTypeFallbackIndex = FindMappedColumnIndex(
            columnSnapshot,
            EntityTypeFallbackColumnNames,
            controlIndexes);
        RelationshipIdIndex = FindMappedColumnIndex(
            columnSnapshot,
            RelationshipIdColumnNames,
            controlIndexes);
        RelationshipTypeIndex = FindMappedColumnIndex(
            columnSnapshot,
            RelationshipTypeColumnNames,
            controlIndexes);
        ControlColumnNames = controlIndexes
            .Select(index => columnSnapshot[index].Name)
            .ToHashSet(StringComparer.Ordinal);
        SchemaJson = WriteSchemaJson(columnSnapshot);
    }

    /// <summary>Gets the ordered result columns.</summary>
    public IReadOnlyList<KustoResultColumn> Columns { get; }

    /// <summary>Gets the generated control-column names.</summary>
    public IReadOnlySet<string> ControlColumnNames { get; }

    /// <summary>Gets the mapped entity identifier index.</summary>
    public int EntityIdIndex { get; }

    /// <summary>Gets the mapped entity label index.</summary>
    public int EntityLabelIndex { get; }

    /// <summary>Gets the mapped entity type fallback index.</summary>
    public int EntityTypeFallbackIndex { get; }

    /// <summary>Gets the mapped entity type index.</summary>
    public int EntityTypeIndex { get; }

    /// <summary>Gets whether the table contains nodes or edges.</summary>
    public KustoGraphExportTableKind Kind { get; }

    /// <summary>Gets the generated node hash index.</summary>
    public int NodeHashIndex { get; }

    /// <summary>Gets the mapped relationship identifier index.</summary>
    public int RelationshipIdIndex { get; }

    /// <summary>Gets the mapped relationship type index.</summary>
    public int RelationshipTypeIndex { get; }

    /// <summary>Gets the canonical table schema JSON.</summary>
    public string SchemaJson { get; }

    /// <summary>Gets the generated source hash index.</summary>
    public int SourceHashIndex { get; }

    /// <summary>Gets the generated result-table name.</summary>
    public string TableName { get; }

    /// <summary>Gets the generated target hash index.</summary>
    public int TargetHashIndex { get; }

    private static int FindColumnIndex(KustoResultColumn[] columns, string columnName)
    {
        for (int index = 0; index < columns.Length; index++)
        {
            if (string.Equals(columns[index].Name, columnName, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindMappedColumnIndex(
        KustoResultColumn[] columns,
        string[] candidateNames,
        HashSet<int> excludedIndexes)
    {
        for (int index = 0; index < columns.Length; index++)
        {
            if (!excludedIndexes.Contains(index)
                && candidateNames.Contains(NormalizeName(columns[index].Name), StringComparer.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static string NormalizeName(string value)
    {
        StringBuilder builder = new(value.Length);
        foreach (char character in value.Where(char.IsLetterOrDigit))
        {
            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    private static string WriteSchemaJson(KustoResultColumn[] columns)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("columns");
            writer.WriteStartArray();
            foreach (KustoResultColumn column in columns)
            {
                writer.WriteStartObject();
                writer.WriteString("name", column.Name);
                writer.WriteString("type", column.TypeName);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
