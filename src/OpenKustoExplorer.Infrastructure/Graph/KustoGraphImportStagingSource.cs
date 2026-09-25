using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Graph;
using OpenKustoExplorer.Portable.Graphs;

namespace OpenKustoExplorer.Infrastructure.Graph;

/// <summary>
/// Spools streamed Kusto graph rows to temporary SQLite storage and exposes validated import sequences.
/// </summary>
internal sealed class KustoGraphImportStagingSource : IKustoGraphImportStagingSource
{
    private const string DefaultEntityType = "Entity";
    private const string DefaultRelationshipType = "RelatedTo";
    private readonly SqliteConnection connection;
    private readonly Guid ingestionId;
    private readonly KustoGraphExportNormalizer normalizer;
    private readonly string sourceNamespace;
    private readonly string stagingDirectoryPath;
    private KustoGraphExportTableMetadata? currentTable;
    private bool isDisposed;
    private GraphIngestion? ingestion;
    private SqliteCommand? insertNodeCommand;
    private SqliteCommand? insertRowCommand;
    private KustoGraphExportTableMetadata? edgeTable;
    private int edgeRowCount;
    private KustoGraphExportTableMetadata? nodeTable;
    private int nodeRowCount;
    private int rowOrdinal;
    private SqliteTransaction? transaction;

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoGraphImportStagingSource"/> class.
    /// </summary>
    /// <param name="plan">The validated graph export plan.</param>
    /// <param name="query">The selected query and source database.</param>
    /// <param name="ingestionId">The identifier reserved for the eventual ingestion.</param>
    internal KustoGraphImportStagingSource(
        KustoGraphQueryPlan plan,
        KustoQueryRequest query,
        Guid ingestionId)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfEqual(ingestionId, Guid.Empty);

        this.ingestionId = ingestionId;
        sourceNamespace = CreateSourceNamespace(query.ClusterUri, query.DatabaseName);
        normalizer = new KustoGraphExportNormalizer(plan, query, ingestionId);
        stagingDirectoryPath = Path.Combine(
            Path.GetTempPath(),
            "OpenKustoExplorer",
            "GraphStaging",
            ingestionId.ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(stagingDirectoryPath);
        string databasePath = Path.Combine(stagingDirectoryPath, "staging.db");
        SqliteConnectionStringBuilder connectionBuilder = new()
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        };
        connection = new SqliteConnection(connectionBuilder.ToString());
        connection.Open();
        CreateSchema(connection);
    }

    /// <inheritdoc />
    public GraphIngestion Ingestion
    {
        get
        {
            EnsureReady();
            return ingestion!;
        }
    }

    /// <inheritdoc />
    public ValueTask WriteBatchAsync(
        KustoGraphExportBatch batch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        cancellationToken.ThrowIfCancellationRequested();
        if (batch.StartsTable)
        {
            BeginTable(batch.Kind, batch.TableName!, batch.Columns);
        }

        foreach (IReadOnlyList<string> row in batch.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteRow(batch.Kind, row);
        }

        if (batch.EndsTable)
        {
            EndTable(batch.Kind);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!isDisposed)
        {
            isDisposed = true;
            DisposeCurrentTable();
            connection.Dispose();

            if (Directory.Exists(stagingDirectoryPath))
            {
                Directory.Delete(stagingDirectoryPath, true);
            }
        }
    }

    /// <inheritdoc />
    public IEnumerable<GraphEntityObservation> GetEntityObservations()
    {
        EnsureReady();
        return ReadEntityObservations();
    }

    /// <inheritdoc />
    public IEnumerable<GraphEvidence> GetEvidence()
    {
        EnsureReady();
        return ReadEvidence();
    }

    /// <inheritdoc />
    public IEnumerable<GraphRelationshipObservation> GetRelationshipObservations()
    {
        EnsureReady();
        return ReadRelationshipObservations();
    }

    /// <summary>
    /// Marks staged rows as eligible for import after parser-level validation succeeds.
    /// </summary>
    /// <param name="ingestion">The completed query provenance.</param>
    /// <param name="exportSummary">The parser-validated export summary.</param>
    public void Complete(GraphIngestion ingestion, KustoGraphExportSummary exportSummary)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(ingestion);
        ArgumentNullException.ThrowIfNull(exportSummary);

        if (this.ingestion is not null)
        {
            throw new InvalidOperationException("The graph staging source is already complete.");
        }

        if (currentTable is not null || nodeTable is null || edgeTable is null)
        {
            throw new InvalidDataException("Both graph export tables must finish before import.");
        }

        if (ingestion.Id != ingestionId)
        {
            throw new ArgumentException("The ingestion identifier does not match the staged export.", nameof(ingestion));
        }

        if (exportSummary.NodeCount != nodeRowCount || exportSummary.EdgeCount != edgeRowCount)
        {
            throw new InvalidDataException("The staged graph row counts do not match the validated export.");
        }

        ValidateEdgeEndpoints();
        this.ingestion = ingestion;
    }

    /// <summary>
    /// Gets distinct staged identities for target-graph duplicate preflight.
    /// </summary>
    /// <returns>The staged identity candidates.</returns>
    public IEnumerable<GraphEntityIdentityCandidate> GetIdentityCandidates()
    {
        EnsureReady();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                entity_kind,
                type_name,
                canonical_id,
                MIN(display_label),
                COUNT(*)
            FROM graph_stage_nodes
            GROUP BY entity_kind, type_name, canonical_id
            ORDER BY type_name, canonical_id;
            """;
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            yield return new GraphEntityIdentityCandidate(
                new GraphEntityKey(
                    (GraphEntityKind)reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    sourceNamespace),
                reader.GetString(3),
                reader.GetInt32(4),
                false);
        }
    }

    /// <summary>
    /// Gets staged identities whose normalized value or display label matches under different inferred types.
    /// </summary>
    /// <returns>The distinct identity conflicts.</returns>
    public IReadOnlyList<GraphEntityIdentityConflict> GetIdentityConflicts()
    {
        EnsureReady();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT match_kind, match_value
            FROM (
                SELECT 0 AS match_kind, normalized_value AS match_value
                FROM (
                    SELECT
                        LOWER(TRIM(canonical_id)) AS normalized_value,
                        entity_kind,
                        type_name,
                        canonical_id
                    FROM graph_stage_nodes
                    GROUP BY normalized_value, entity_kind, type_name, canonical_id
                )
                GROUP BY normalized_value
                HAVING COUNT(*) > 1

                UNION ALL

                SELECT 1 AS match_kind, normalized_value AS match_value
                FROM (
                    SELECT
                        LOWER(TRIM(display_label)) AS normalized_value,
                        entity_kind,
                        type_name,
                        canonical_id
                    FROM graph_stage_nodes
                    GROUP BY normalized_value, entity_kind, type_name, canonical_id
                )
                GROUP BY normalized_value
                HAVING COUNT(*) > 1
            )
            ORDER BY match_kind, match_value;
            """;
        List<(GraphEntityIdentityMatchKind Kind, string Value)> matches = [];
        using (SqliteDataReader reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                matches.Add(((GraphEntityIdentityMatchKind)reader.GetInt32(0), reader.GetString(1)));
            }
        }

        HashSet<string> candidateSets = new(StringComparer.Ordinal);
        List<GraphEntityIdentityConflict> conflicts = [];
        foreach ((GraphEntityIdentityMatchKind matchKind, string matchValue) in matches)
        {
            GraphEntityIdentityCandidate[] candidates = ReadIdentityCandidates(
                connection,
                sourceNamespace,
                matchKind,
                matchValue);
            string candidateSet = CreateCandidateSetKey(candidates);
            if (candidateSets.Add(candidateSet))
            {
                GraphEntityIdentityCandidate suggestedCandidate = SelectSuggestedCandidate(candidates);
                conflicts.Add(new GraphEntityIdentityConflict(
                    matchKind,
                    matchValue,
                    candidates,
                    suggestedCandidate.Entity));
            }
        }

        return conflicts.AsReadOnly();
    }

    /// <summary>
    /// Remaps staged candidates in one approved conflict to its suggested identity.
    /// </summary>
    /// <param name="conflict">The approved identity conflict.</param>
    public void MergeIdentityConflict(GraphEntityIdentityConflict conflict)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        EnsureReady();

        if (!string.Equals(
            conflict.SuggestedEntity.SourceNamespace,
            sourceNamespace,
            StringComparison.Ordinal))
        {
            throw new ArgumentException("The merged identity must use the staged source namespace.", nameof(conflict));
        }

        using SqliteTransaction mergeTransaction = connection.BeginTransaction();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = mergeTransaction;
        command.CommandText = """
            UPDATE graph_stage_nodes
            SET
                entity_kind = $targetKind,
                type_name = $targetTypeName,
                canonical_id = $targetCanonicalId
            WHERE entity_kind = $candidateKind
                AND type_name = $candidateTypeName
                AND canonical_id = $candidateCanonicalId;
            """;
        command.Parameters.AddWithValue("$targetKind", (int)conflict.SuggestedEntity.Kind);
        command.Parameters.AddWithValue("$targetTypeName", conflict.SuggestedEntity.TypeName);
        command.Parameters.AddWithValue("$targetCanonicalId", conflict.SuggestedEntity.CanonicalId);
        command.Parameters.Add("$candidateKind", SqliteType.Integer);
        command.Parameters.Add("$candidateTypeName", SqliteType.Text);
        command.Parameters.Add("$candidateCanonicalId", SqliteType.Text);

        foreach (GraphEntityKey candidate in conflict.Candidates
            .Where(candidate => !candidate.IsExisting)
            .Select(candidate => candidate.Entity))
        {
            command.Parameters["$candidateKind"].Value = (int)candidate.Kind;
            command.Parameters["$candidateTypeName"].Value = candidate.TypeName;
            command.Parameters["$candidateCanonicalId"].Value = candidate.CanonicalId;
            command.ExecuteNonQuery();
        }

        mergeTransaction.Commit();
    }

    private static string CreateCandidateSetKey(IEnumerable<GraphEntityIdentityCandidate> candidates)
    {
        return string.Join(
            "\u001e",
            candidates
                .Select(candidate => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{(int)candidate.Entity.Kind}\u001f{candidate.Entity.TypeName}\u001f{candidate.Entity.CanonicalId}"))
                .Order(StringComparer.Ordinal));
    }

    private static GraphEntityIdentityCandidate SelectSuggestedCandidate(
        IEnumerable<GraphEntityIdentityCandidate> candidates)
    {
        return candidates
            .OrderByDescending(candidate => candidate.IsExisting)
            .ThenByDescending(candidate => candidate.OccurrenceCount)
            .ThenBy(candidate => candidate.Entity.Kind == GraphEntityKind.Unknown)
            .ThenBy(candidate => !string.Equals(
                candidate.Entity.TypeName,
                candidate.Entity.Kind.ToString(),
                StringComparison.OrdinalIgnoreCase))
            .ThenBy(candidate => string.Equals(
                candidate.Entity.TypeName,
                DefaultEntityType,
                StringComparison.Ordinal))
            .ThenBy(candidate => candidate.Entity.TypeName, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static GraphEntityIdentityCandidate[] ReadIdentityCandidates(
        SqliteConnection connection,
        string sourceNamespace,
        GraphEntityIdentityMatchKind matchKind,
        string matchValue)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = matchKind == GraphEntityIdentityMatchKind.CanonicalId
            ? """
                SELECT
                    entity_kind,
                    type_name,
                    canonical_id,
                    MIN(display_label),
                    COUNT(*)
                FROM graph_stage_nodes
                WHERE LOWER(TRIM(canonical_id)) = $matchValue
                GROUP BY entity_kind, type_name, canonical_id
                ORDER BY type_name, canonical_id;
                """
            : """
                SELECT
                    entity_kind,
                    type_name,
                    canonical_id,
                    MIN(display_label),
                    COUNT(*)
                FROM graph_stage_nodes
                WHERE LOWER(TRIM(display_label)) = $matchValue
                GROUP BY entity_kind, type_name, canonical_id
                ORDER BY type_name, canonical_id;
                """;
        command.Parameters.AddWithValue("$matchValue", matchValue);
        using SqliteDataReader reader = command.ExecuteReader();
        List<GraphEntityIdentityCandidate> candidates = [];

        while (reader.Read())
        {
            candidates.Add(new GraphEntityIdentityCandidate(
                new GraphEntityKey(
                    (GraphEntityKind)reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    sourceNamespace),
                reader.GetString(3),
                reader.GetInt32(4),
                false));
        }

        return candidates.ToArray();
    }

    private static Guid CreateObservationId(string occurrenceId)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(occurrenceId));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static void CreateSchema(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = OFF;
            PRAGMA synchronous = OFF;
            PRAGMA temp_store = FILE;

            CREATE TABLE graph_stage_rows (
                table_kind INTEGER NOT NULL,
                row_ordinal INTEGER NOT NULL,
                row_json TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                source_hash TEXT NOT NULL,
                target_hash TEXT NOT NULL,
                relationship_type TEXT NOT NULL,
                discriminator TEXT NOT NULL,
                PRIMARY KEY (table_kind, row_ordinal)
            ) WITHOUT ROWID;

            CREATE TABLE graph_stage_nodes (
                node_hash TEXT NOT NULL PRIMARY KEY,
                row_ordinal INTEGER NOT NULL UNIQUE,
                entity_kind INTEGER NOT NULL,
                type_name TEXT NOT NULL,
                source_type_name TEXT NOT NULL,
                canonical_id TEXT NOT NULL,
                display_label TEXT NOT NULL
            ) WITHOUT ROWID;
            """;
        command.ExecuteNonQuery();
    }

    private static SqliteCommand CreateInsertNodeCommand(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO graph_stage_nodes (
                node_hash,
                row_ordinal,
                entity_kind,
                type_name,
                source_type_name,
                canonical_id,
                display_label
            ) VALUES (
                $nodeHash,
                $rowOrdinal,
                $entityKind,
                $typeName,
                $sourceTypeName,
                $canonicalId,
                $displayLabel
            );
            """;
        command.Parameters.Add("$nodeHash", SqliteType.Text);
        command.Parameters.Add("$rowOrdinal", SqliteType.Integer);
        command.Parameters.Add("$entityKind", SqliteType.Integer);
        command.Parameters.Add("$typeName", SqliteType.Text);
        command.Parameters.Add("$sourceTypeName", SqliteType.Text);
        command.Parameters.Add("$canonicalId", SqliteType.Text);
        command.Parameters.Add("$displayLabel", SqliteType.Text);
        command.Prepare();
        return command;
    }

    private static SqliteCommand CreateInsertRowCommand(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO graph_stage_rows (
                table_kind,
                row_ordinal,
                row_json,
                content_hash,
                source_hash,
                target_hash,
                relationship_type,
                discriminator
            ) VALUES (
                $tableKind,
                $rowOrdinal,
                $rowJson,
                $contentHash,
                $sourceHash,
                $targetHash,
                $relationshipType,
                $discriminator
            );
            """;
        command.Parameters.Add("$tableKind", SqliteType.Integer);
        command.Parameters.Add("$rowOrdinal", SqliteType.Integer);
        command.Parameters.Add("$rowJson", SqliteType.Text);
        command.Parameters.Add("$contentHash", SqliteType.Text);
        command.Parameters.Add("$sourceHash", SqliteType.Text);
        command.Parameters.Add("$targetHash", SqliteType.Text);
        command.Parameters.Add("$relationshipType", SqliteType.Text);
        command.Parameters.Add("$discriminator", SqliteType.Text);
        command.Prepare();
        return command;
    }

    private static string CreateSourceNamespace(Uri clusterUri, string databaseName)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{clusterUri.Host.ToLowerInvariant()}/{databaseName.Trim().ToLowerInvariant()}");
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

    private void BeginTable(
        KustoGraphExportTableKind kind,
        string tableName,
        IReadOnlyList<KustoResultColumn> columns)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentNullException.ThrowIfNull(columns);

        if (ingestion is not null)
        {
            throw new InvalidOperationException("The completed graph staging source cannot receive more tables.");
        }

        if (currentTable is not null)
        {
            throw new InvalidOperationException("A graph export table is already being staged.");
        }

        if ((kind == KustoGraphExportTableKind.Nodes && nodeTable is not null)
            || (kind == KustoGraphExportTableKind.Edges && edgeTable is not null))
        {
            throw new InvalidDataException($"Graph export table '{tableName}' was staged more than once.");
        }

        currentTable = normalizer.CreateTableMetadata(kind, tableName, columns);
        rowOrdinal = 0;
        transaction = connection.BeginTransaction();
        insertRowCommand = CreateInsertRowCommand(connection, transaction);

        if (kind == KustoGraphExportTableKind.Nodes)
        {
            insertNodeCommand = CreateInsertNodeCommand(connection, transaction);
        }
    }

    private void EndTable(KustoGraphExportTableKind kind)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        KustoGraphExportTableMetadata metadata = GetCurrentTable(kind);

        try
        {
            transaction!.Commit();
            if (kind == KustoGraphExportTableKind.Nodes)
            {
                nodeTable = metadata;
                nodeRowCount = rowOrdinal;
            }
            else
            {
                edgeTable = metadata;
                edgeRowCount = rowOrdinal;
            }
        }
        finally
        {
            DisposeCurrentTable();
        }
    }

    private void WriteRow(KustoGraphExportTableKind kind, IReadOnlyList<string> values)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(values);
        KustoGraphExportTableMetadata metadata = GetCurrentTable(kind);
        int maximumRowCount = kind == KustoGraphExportTableKind.Nodes
            ? GraphStorageLimits.MaximumEntityCount
            : GraphStorageLimits.MaximumRelationshipCount;
        if (rowOrdinal >= maximumRowCount)
        {
            throw new InvalidDataException(
                $"The graph export exceeds the {maximumRowCount:N0} {kind.ToString().ToLowerInvariant()} limit.");
        }

        KustoGraphExportRow row = normalizer.NormalizeRow(metadata, values, rowOrdinal);
        if (kind == KustoGraphExportTableKind.Nodes)
        {
            StageNode(row);
        }

        SqliteCommand command = insertRowCommand!;
        command.Parameters["$tableKind"].Value = (int)kind;
        command.Parameters["$rowOrdinal"].Value = rowOrdinal;
        command.Parameters["$rowJson"].Value = row.RowJson;
        command.Parameters["$contentHash"].Value = row.ContentHash;
        command.Parameters["$sourceHash"].Value = row.SourceHash;
        command.Parameters["$targetHash"].Value = row.TargetHash;
        command.Parameters["$relationshipType"].Value = row.RelationshipType;
        command.Parameters["$discriminator"].Value = row.Discriminator;
        command.ExecuteNonQuery();
        rowOrdinal++;
    }

    private void DisposeCurrentTable()
    {
        insertNodeCommand?.Dispose();
        insertNodeCommand = null;
        insertRowCommand?.Dispose();
        insertRowCommand = null;
        transaction?.Dispose();
        transaction = null;
        currentTable = null;
        rowOrdinal = 0;
    }

    private void EnsureReady()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (ingestion is null || nodeTable is null || edgeTable is null)
        {
            throw new InvalidOperationException("The staged graph export has not completed validation.");
        }
    }

    private KustoGraphExportTableMetadata GetCurrentTable(KustoGraphExportTableKind kind)
    {
        if (currentTable is null || currentTable.Kind != kind)
        {
            throw new InvalidOperationException($"No {kind} graph export table is currently being staged.");
        }

        return currentTable;
    }

    private string GetOccurrenceId(KustoGraphExportTableKind kind, int ordinal)
    {
        string tableCode = kind == KustoGraphExportTableKind.Nodes ? "n" : "e";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{ingestionId:N}:{tableCode}:{ordinal}");
    }

    private IEnumerable<GraphEntityObservation> ReadEntityObservations()
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                row.row_ordinal,
                row.row_json,
                node.entity_kind,
                node.type_name,
                node.source_type_name,
                node.canonical_id,
                node.display_label
            FROM graph_stage_nodes AS node
            INNER JOIN graph_stage_rows AS row
                ON row.table_kind = 0
                AND row.row_ordinal = node.row_ordinal
            ORDER BY row.row_ordinal;
            """;
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            int ordinal = reader.GetInt32(0);
            string rowJson = reader.GetString(1);
            GraphEntityKind entityKind = (GraphEntityKind)reader.GetInt32(2);
            string typeName = reader.GetString(3);
            string sourceTypeName = reader.GetString(4);
            string canonicalId = reader.GetString(5);
            string displayLabel = reader.GetString(6);
            string occurrenceId = GetOccurrenceId(KustoGraphExportTableKind.Nodes, ordinal);
            GraphEntityKey entity = new(entityKind, typeName, canonicalId, sourceNamespace);
            string[] sourceLabels = string.Equals(sourceTypeName, DefaultEntityType, StringComparison.Ordinal)
                ? []
                : [sourceTypeName];

            yield return new GraphEntityObservation(
                CreateObservationId(string.Concat("node:", occurrenceId)),
                entity,
                displayLabel,
                sourceLabels,
                ReadProperties(rowJson, nodeTable!.ControlColumnNames),
                new GraphTemporalInterval(ingestion!.CompletedAtUtc),
                [occurrenceId]);
        }
    }

    private IEnumerable<GraphEvidence> ReadEvidence()
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT table_kind, row_ordinal, row_json, content_hash
            FROM graph_stage_rows
            ORDER BY table_kind, row_ordinal;
            """;
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            KustoGraphExportTableKind kind = (KustoGraphExportTableKind)reader.GetInt32(0);
            int ordinal = reader.GetInt32(1);
            KustoGraphExportTableMetadata metadata = kind == KustoGraphExportTableKind.Nodes
                ? nodeTable!
                : edgeTable!;

            yield return new GraphEvidence(
                GetOccurrenceId(kind, ordinal),
                reader.GetString(3),
                metadata.TableName,
                ordinal,
                metadata.SchemaJson,
                reader.GetString(2));
        }
    }

    private IEnumerable<GraphRelationshipObservation> ReadRelationshipObservations()
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                edge.row_ordinal,
                edge.row_json,
                edge.relationship_type,
                edge.discriminator,
                source.entity_kind,
                source.type_name,
                source.canonical_id,
                target.entity_kind,
                target.type_name,
                target.canonical_id
            FROM graph_stage_rows AS edge
            INNER JOIN graph_stage_nodes AS source
                ON source.node_hash = edge.source_hash
            INNER JOIN graph_stage_nodes AS target
                ON target.node_hash = edge.target_hash
            WHERE edge.table_kind = 1
            ORDER BY edge.row_ordinal;
            """;
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            int ordinal = reader.GetInt32(0);
            string rowJson = reader.GetString(1);
            string relationshipType = reader.GetString(2);
            string discriminator = reader.GetString(3);
            GraphEntityKey source = new(
                (GraphEntityKind)reader.GetInt32(4),
                reader.GetString(5),
                reader.GetString(6),
                sourceNamespace);
            GraphEntityKey target = new(
                (GraphEntityKind)reader.GetInt32(7),
                reader.GetString(8),
                reader.GetString(9),
                sourceNamespace);
            string occurrenceId = GetOccurrenceId(KustoGraphExportTableKind.Edges, ordinal);
            string[] sourceLabels = string.Equals(
                relationshipType,
                DefaultRelationshipType,
                StringComparison.Ordinal)
                ? []
                : [relationshipType];

            yield return new GraphRelationshipObservation(
                CreateObservationId(string.Concat("edge:", occurrenceId)),
                new GraphRelationshipKey(source, target, relationshipType, discriminator),
                sourceLabels,
                ReadProperties(rowJson, edgeTable!.ControlColumnNames),
                new GraphTemporalInterval(ingestion!.CompletedAtUtc),
                [occurrenceId]);
        }
    }

    private void StageNode(KustoGraphExportRow row)
    {
        SqliteCommand command = insertNodeCommand!;
        command.Parameters["$nodeHash"].Value = row.NodeHash;
        command.Parameters["$rowOrdinal"].Value = rowOrdinal;
        command.Parameters["$entityKind"].Value = (int)row.EntityKind;
        command.Parameters["$typeName"].Value = row.TypeName;
        command.Parameters["$sourceTypeName"].Value = row.SourceTypeName;
        command.Parameters["$canonicalId"].Value = row.CanonicalId;
        command.Parameters["$displayLabel"].Value = row.DisplayLabel;
        command.ExecuteNonQuery();
    }

    private void ValidateEdgeEndpoints()
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM graph_stage_rows AS edge
            LEFT JOIN graph_stage_nodes AS source
                ON source.node_hash = edge.source_hash
            LEFT JOIN graph_stage_nodes AS target
                ON target.node_hash = edge.target_hash
            WHERE edge.table_kind = 1
                AND (source.node_hash IS NULL OR target.node_hash IS NULL);
            """;
        long missingEndpointCount = (long)(command.ExecuteScalar() ?? 0L);

        if (missingEndpointCount > 0)
        {
            throw new InvalidDataException(
                $"The graph export contains {missingEndpointCount:N0} edge rows with unresolved endpoints.");
        }
    }
}
