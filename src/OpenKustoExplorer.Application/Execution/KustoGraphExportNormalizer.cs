using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Graph;

namespace OpenKustoExplorer.Application.Execution;

/// <summary>
/// Normalizes streamed Kusto graph tables into host-independent staged rows and graph observations.
/// </summary>
public sealed class KustoGraphExportNormalizer
{
    private const string DefaultEntityType = "Entity";
    private const string DefaultRelationshipType = "RelatedTo";
    private readonly KustoGraphQueryPlan plan;

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoGraphExportNormalizer"/> class.
    /// </summary>
    /// <param name="plan">The validated graph export plan.</param>
    /// <param name="query">The selected query and source database.</param>
    /// <param name="ingestionId">The identifier reserved for the ingestion.</param>
    public KustoGraphExportNormalizer(
        KustoGraphQueryPlan plan,
        KustoQueryRequest query,
        Guid ingestionId)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfEqual(ingestionId, Guid.Empty);

        this.plan = plan;
        IngestionId = ingestionId;
        SourceNamespace = string.Create(
            CultureInfo.InvariantCulture,
            $"{query.ClusterUri.Host.ToLowerInvariant()}/{query.DatabaseName.Trim().ToLowerInvariant()}");
    }

    /// <summary>
    /// Gets the identifier reserved for the ingestion.
    /// </summary>
    public Guid IngestionId { get; }

    /// <summary>
    /// Gets the normalized cluster and database namespace for entity identities.
    /// </summary>
    public string SourceNamespace { get; }

    /// <summary>
    /// Creates and validates metadata for one streamed graph table.
    /// </summary>
    /// <param name="kind">Whether the table contains nodes or edges.</param>
    /// <param name="tableName">The generated result-table name.</param>
    /// <param name="columns">The ordered result columns.</param>
    /// <returns>The reusable table metadata.</returns>
    public KustoGraphExportTableMetadata CreateTableMetadata(
        KustoGraphExportTableKind kind,
        string tableName,
        IReadOnlyList<KustoResultColumn> columns)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentNullException.ThrowIfNull(columns);
        string expectedTableName = kind == KustoGraphExportTableKind.Nodes
            ? plan.NodeTableName
            : plan.EdgeTableName;
        if (!string.Equals(tableName, expectedTableName, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unexpected graph export table '{tableName}'.");
        }

        return new KustoGraphExportTableMetadata(kind, tableName, columns, plan);
    }

    /// <summary>
    /// Creates one entity observation from a normalized node row.
    /// </summary>
    /// <param name="row">The normalized node row.</param>
    /// <param name="metadata">The node table metadata.</param>
    /// <param name="completedAtUtc">The ingestion completion time.</param>
    /// <param name="entityOverride">An optional analyst-approved merged identity.</param>
    /// <returns>The entity observation.</returns>
    public GraphEntityObservation CreateEntityObservation(
        KustoGraphExportRow row,
        KustoGraphExportTableMetadata metadata,
        DateTimeOffset completedAtUtc,
        GraphEntityKey? entityOverride = null)
    {
        EnsureRowKind(row, metadata, KustoGraphExportTableKind.Nodes);
        string occurrenceId = GetOccurrenceId(row.Kind, row.RowOrdinal);
        GraphEntityKey entity = entityOverride ?? CreateEntityKey(row);
        string[] sourceLabels = string.Equals(row.SourceTypeName, DefaultEntityType, StringComparison.Ordinal)
            ? []
            : [row.SourceTypeName];
        return new GraphEntityObservation(
            CreateObservationId(string.Concat("node:", occurrenceId)),
            entity,
            row.DisplayLabel,
            sourceLabels,
            ReadProperties(row.RowJson, metadata.ControlColumnNames),
            new GraphTemporalInterval(completedAtUtc),
            [occurrenceId]);
    }

    /// <summary>
    /// Creates one raw evidence occurrence from a normalized row.
    /// </summary>
    /// <param name="row">The normalized graph row.</param>
    /// <param name="metadata">The source table metadata.</param>
    /// <returns>The raw evidence occurrence.</returns>
    public GraphEvidence CreateEvidence(
        KustoGraphExportRow row,
        KustoGraphExportTableMetadata metadata)
    {
        EnsureRowKind(row, metadata, row.Kind);
        return new GraphEvidence(
            GetOccurrenceId(row.Kind, row.RowOrdinal),
            row.ContentHash,
            metadata.TableName,
            row.RowOrdinal,
            metadata.SchemaJson,
            row.RowJson);
    }

    /// <summary>
    /// Creates the source-scoped entity identity represented by a normalized node row.
    /// </summary>
    /// <param name="row">The normalized node row.</param>
    /// <returns>The entity identity.</returns>
    public GraphEntityKey CreateEntityKey(KustoGraphExportRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Kind != KustoGraphExportTableKind.Nodes)
        {
            throw new ArgumentException("Only a node row defines an entity identity.", nameof(row));
        }

        return new GraphEntityKey(
            row.EntityKind,
            row.TypeName,
            row.CanonicalId,
            SourceNamespace);
    }

    /// <summary>
    /// Creates one relationship observation from a normalized edge and its resolved endpoint rows.
    /// </summary>
    /// <param name="row">The normalized edge row.</param>
    /// <param name="metadata">The edge table metadata.</param>
    /// <param name="source">The resolved source entity.</param>
    /// <param name="target">The resolved target entity.</param>
    /// <param name="completedAtUtc">The ingestion completion time.</param>
    /// <returns>The relationship observation.</returns>
    public GraphRelationshipObservation CreateRelationshipObservation(
        KustoGraphExportRow row,
        KustoGraphExportTableMetadata metadata,
        GraphEntityKey source,
        GraphEntityKey target,
        DateTimeOffset completedAtUtc)
    {
        EnsureRowKind(row, metadata, KustoGraphExportTableKind.Edges);
        string occurrenceId = GetOccurrenceId(row.Kind, row.RowOrdinal);
        string[] sourceLabels = string.Equals(
            row.RelationshipType,
            DefaultRelationshipType,
            StringComparison.Ordinal)
            ? []
            : [row.RelationshipType];
        return new GraphRelationshipObservation(
            CreateObservationId(string.Concat("edge:", occurrenceId)),
            new GraphRelationshipKey(source, target, row.RelationshipType, row.Discriminator),
            sourceLabels,
            ReadProperties(row.RowJson, metadata.ControlColumnNames),
            new GraphTemporalInterval(completedAtUtc),
            [occurrenceId]);
    }

    /// <summary>
    /// Normalizes one ordered export row using validated table metadata.
    /// </summary>
    /// <param name="metadata">The source table metadata.</param>
    /// <param name="values">The ordered invariant string cell values.</param>
    /// <param name="rowOrdinal">The zero-based row position.</param>
    /// <returns>The normalized staged row.</returns>
    public KustoGraphExportRow NormalizeRow(
        KustoGraphExportTableMetadata metadata,
        IReadOnlyList<string> values,
        int rowOrdinal)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentOutOfRangeException.ThrowIfNegative(rowOrdinal);
        string expectedTableName = metadata.Kind == KustoGraphExportTableKind.Nodes
            ? plan.NodeTableName
            : plan.EdgeTableName;
        if (!string.Equals(metadata.TableName, expectedTableName, StringComparison.Ordinal))
        {
            throw new ArgumentException("The table metadata does not belong to this graph plan.", nameof(metadata));
        }

        if (values.Count != metadata.Columns.Count)
        {
            throw new InvalidDataException(
                $"Graph export table '{metadata.TableName}' returned {values.Count} values for "
                + $"{metadata.Columns.Count} columns.");
        }

        string rowJson = WriteRowJson(metadata.Columns, values);
        string contentHash = ComputeContentHash(metadata.SchemaJson, rowJson);
        return metadata.Kind == KustoGraphExportTableKind.Nodes
            ? NormalizeNode(metadata, values, rowOrdinal, rowJson, contentHash)
            : NormalizeEdge(metadata, values, rowOrdinal, rowJson, contentHash);
    }

    private static string ComputeContentHash(string schemaJson, string rowJson)
    {
        string content = string.Concat(schemaJson, "\n", rowJson);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static Guid CreateObservationId(string occurrenceId)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(occurrenceId));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static void EnsureRowKind(
        KustoGraphExportRow row,
        KustoGraphExportTableMetadata metadata,
        KustoGraphExportTableKind expectedKind)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(metadata);
        if (row.Kind != expectedKind || metadata.Kind != expectedKind)
        {
            throw new ArgumentException("The normalized row and table kinds do not match.", nameof(row));
        }
    }

    private static GraphEntityKind InferEntityKind(string typeName)
    {
        string normalizedType = NormalizeName(typeName);
        return normalizedType switch
        {
            "account" or "githubuser" or "person" => GraphEntityKind.User,
            "computer" or "machine" => GraphEntityKind.Host,
            "ip" => GraphEntityKind.IpAddress,
            "principal" => GraphEntityKind.Identity,
            "publickey" or "sshkey" => GraphEntityKind.Credential,
            "resource" => GraphEntityKind.CloudResource,
            "tenant" => GraphEntityKind.CloudTenant,
            "vm" => GraphEntityKind.VirtualMachine,
            _ => Enum.TryParse(normalizedType, true, out GraphEntityKind parsedKind)
                ? parsedKind
                : GraphEntityKind.Unknown,
        };
    }

    private static string GetOptionalValue(IReadOnlyList<string> values, int index)
    {
        return index >= 0 ? values[index] : string.Empty;
    }

    private static string GetRequiredValue(
        IReadOnlyList<string> values,
        int index,
        string valueDescription)
    {
        string value = GetOptionalValue(values, index);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"A graph export row contains no {valueDescription}.");
        }

        return value.Trim();
    }

    private static KustoGraphExportRow NormalizeEdge(
        KustoGraphExportTableMetadata metadata,
        IReadOnlyList<string> values,
        int rowOrdinal,
        string rowJson,
        string contentHash)
    {
        string relationshipType = GetOptionalValue(values, metadata.RelationshipTypeIndex);
        relationshipType = string.IsNullOrWhiteSpace(relationshipType)
            ? DefaultRelationshipType
            : relationshipType.Trim();
        string discriminator = GetOptionalValue(values, metadata.RelationshipIdIndex);
        discriminator = string.IsNullOrWhiteSpace(discriminator) ? contentHash : discriminator.Trim();
        return KustoGraphExportRow.CreateEdge(
            rowOrdinal,
            rowJson,
            contentHash,
            GetRequiredValue(values, metadata.SourceHashIndex, "source hash"),
            GetRequiredValue(values, metadata.TargetHashIndex, "target hash"),
            relationshipType,
            discriminator);
    }

    private static KustoGraphExportRow NormalizeNode(
        KustoGraphExportTableMetadata metadata,
        IReadOnlyList<string> values,
        int rowOrdinal,
        string rowJson,
        string contentHash)
    {
        string nodeHash = GetRequiredValue(values, metadata.NodeHashIndex, "node hash");
        string typeName = GetOptionalValue(values, metadata.EntityTypeIndex);
        bool usesTypeFallback = string.IsNullOrWhiteSpace(typeName);
        if (usesTypeFallback)
        {
            typeName = GetOptionalValue(values, metadata.EntityTypeFallbackIndex);
        }

        typeName = string.IsNullOrWhiteSpace(typeName) ? DefaultEntityType : typeName.Trim();
        string canonicalId = GetOptionalValue(values, metadata.EntityIdIndex);
        canonicalId = string.IsNullOrWhiteSpace(canonicalId) ? nodeHash : canonicalId.Trim();
        string displayLabel = usesTypeFallback
            ? GetOptionalValue(values, metadata.EntityIdIndex)
            : GetOptionalValue(values, metadata.EntityLabelIndex);
        if (string.IsNullOrWhiteSpace(displayLabel))
        {
            displayLabel = GetOptionalValue(values, metadata.EntityIdIndex);
        }

        if (string.IsNullOrWhiteSpace(displayLabel))
        {
            displayLabel = values
                .Where((value, index) => index != metadata.NodeHashIndex && !string.IsNullOrWhiteSpace(value))
                .FirstOrDefault() ?? canonicalId;
        }

        return KustoGraphExportRow.CreateNode(
            rowOrdinal,
            rowJson,
            contentHash,
            nodeHash,
            InferEntityKind(typeName),
            typeName,
            canonicalId,
            displayLabel.Trim());
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

    private static Dictionary<string, string> ReadProperties(
        string rowJson,
        IReadOnlySet<string> controlColumnNames)
    {
        Dictionary<string, string> properties = new(StringComparer.Ordinal);
        using JsonDocument document = JsonDocument.Parse(rowJson);
        foreach (JsonProperty property in document.RootElement
            .EnumerateObject()
            .Where(property => !controlColumnNames.Contains(property.Name)))
        {
            properties.Add(property.Name, property.Value.GetString() ?? string.Empty);
        }

        return properties;
    }

    private static string WriteRowJson(
        IReadOnlyList<KustoResultColumn> columns,
        IReadOnlyList<string> values)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartObject();
            for (int index = 0; index < columns.Count; index++)
            {
                writer.WriteString(columns[index].Name, values[index]);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private string GetOccurrenceId(KustoGraphExportTableKind kind, int ordinal)
    {
        string tableCode = kind == KustoGraphExportTableKind.Nodes ? "n" : "e";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{IngestionId:N}:{tableCode}:{ordinal}");
    }
}
