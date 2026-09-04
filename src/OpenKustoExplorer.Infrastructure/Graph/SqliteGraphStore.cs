using Microsoft.Data.Sqlite;
using OpenKustoExplorer.Graph;
using OpenKustoExplorer.Graph.Query;
using OpenKustoExplorer.Infrastructure.Graph.Query;

namespace OpenKustoExplorer.Infrastructure.Graph;

/// <summary>
/// Persists one app-wide investigation graph in a transactional, generation-aware SQLite database.
/// </summary>
public sealed class SqliteGraphStore : IGraphStore, IGraphQueryService, IDisposable
{
    private const string DefaultGraphName = "Default graph";
    private const int MaximumNeighborhoodDepth = 10;
    private const int MaximumViewportEntityCount = 500;
    private const int MaximumViewportRelationshipCount = 2_000;
    private const int MaximumSearchResults = 500;
    private readonly string connectionString;
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteGraphStore"/> class using local application data.
    /// </summary>
    public SqliteGraphStore()
        : this(GetDefaultFilePath())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteGraphStore"/> class.
    /// </summary>
    /// <param name="filePath">The absolute SQLite database path.</param>
    public SqliteGraphStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        string fullPath = Path.GetFullPath(filePath);
        string directoryPath = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("The graph database path has no directory.");
        Directory.CreateDirectory(directoryPath);
        SqliteConnectionStringBuilder connectionBuilder = new()
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        };
        connectionString = connectionBuilder.ToString();

        using SqliteConnection connection = OpenConnection();
        using (SqliteCommand journalCommand = connection.CreateCommand())
        {
            journalCommand.CommandText = "PRAGMA journal_mode = WAL;";
            journalCommand.ExecuteScalar();
        }

        GraphSqliteSchema.EnsureCreated(connection);
    }

    /// <inheritdoc />
    public async Task<GraphStateSummary> ClearAsync(CancellationToken cancellationToken = default)
    {
        return await ClearCoreAsync(null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<GraphStateSummary> ClearAsync(
        GraphWriteTarget target,
        CancellationToken cancellationToken = default)
    {
        return await ClearCoreAsync(target, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<GraphCatalog> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteConnection connection = OpenConnection();
        return Task.FromResult(GraphSqliteCatalogReader.ReadCatalog(connection));
    }

    /// <inheritdoc />
    public async Task<GraphStateSummary> CreateGraphAsync(
        string name,
        string? description,
        CancellationToken cancellationToken = default)
    {
        string normalizedName = GraphCatalogMetadata.NormalizeName(name);
        string normalizedNameKey = GraphCatalogMetadata.NormalizeNameKey(name);
        string normalizedDescription = GraphCatalogMetadata.NormalizeDescription(description);
        ObjectDisposedException.ThrowIf(isDisposed, this);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using SqliteConnection connection = OpenConnection();
            await using SqliteTransaction transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            GraphSnapshot snapshot = GraphSqliteCatalogWriter.CreateGraph(
                connection,
                transaction,
                normalizedName,
                normalizedNameKey,
                normalizedDescription,
                DateTimeOffset.UtcNow);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return GraphSqliteReader.ReadState(connection, snapshot);
        }
        finally
        {
            writeGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<GraphCatalogEntry> UpdateGraphAsync(
        Guid graphId,
        string name,
        string? description,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(graphId, Guid.Empty);
        string normalizedName = GraphCatalogMetadata.NormalizeName(name);
        string normalizedNameKey = GraphCatalogMetadata.NormalizeNameKey(name);
        string normalizedDescription = GraphCatalogMetadata.NormalizeDescription(description);
        ObjectDisposedException.ThrowIf(isDisposed, this);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using SqliteConnection connection = OpenConnection();
            await using SqliteTransaction transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            GraphSqliteCatalogWriter.UpdateGraph(
                connection,
                transaction,
                graphId,
                normalizedName,
                normalizedNameKey,
                normalizedDescription,
                DateTimeOffset.UtcNow);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return GraphSqliteCatalogReader.ReadCatalog(connection).Graphs.Single(graph => graph.GraphId == graphId);
        }
        finally
        {
            writeGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<GraphEntitySummary> SetEntityDisplayLabelAsync(
        GraphSnapshot snapshot,
        GraphEntityKey entity,
        string displayLabel,
        CancellationToken cancellationToken = default)
    {
        string normalizedLabel = GraphEntityDisplayLabel.Normalize(displayLabel);
        ObjectDisposedException.ThrowIf(isDisposed, this);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using SqliteConnection connection = OpenConnection();
            await using SqliteTransaction transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            GraphSqliteCatalogReader.ValidateSnapshot(connection, snapshot, transaction);
            GraphSqliteWriter.SetEntityDisplayLabel(
                connection,
                transaction,
                snapshot,
                entity,
                normalizedLabel,
                DateTimeOffset.UtcNow);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return GraphSqliteReader.ReadEntityDetails(connection, snapshot, entity)?.Summary
                ?? throw new InvalidOperationException("The renamed graph node could not be reloaded.");
        }
        finally
        {
            writeGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<GraphStateSummary> ActivateGraphAsync(
        Guid graphId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(graphId, Guid.Empty);
        ObjectDisposedException.ThrowIf(isDisposed, this);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using SqliteConnection connection = OpenConnection();
            await using SqliteTransaction transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            GraphSqliteCatalogWriter.ActivateGraph(connection, transaction, graphId, DateTimeOffset.UtcNow);
            GraphSnapshot snapshot = GraphSqliteCatalogReader.ReadSnapshot(connection, graphId, transaction);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return GraphSqliteReader.ReadState(connection, snapshot);
        }
        finally
        {
            writeGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<GraphCatalog> DeleteGraphAsync(
        Guid graphId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(graphId, Guid.Empty);
        ObjectDisposedException.ThrowIf(isDisposed, this);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using SqliteConnection connection = OpenConnection();
            await using SqliteTransaction transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            GraphSqliteCatalogWriter.DeleteGraph(connection, transaction, graphId);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return GraphSqliteCatalogReader.ReadCatalog(connection);
        }
        finally
        {
            writeGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<GraphCatalog> DeleteAllGraphsAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using SqliteConnection connection = OpenConnection();
            await using SqliteTransaction transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            string temporaryName = $"Reset graph {Guid.NewGuid():N}";
            GraphSnapshot replacement = GraphSqliteCatalogWriter.CreateGraph(
                connection,
                transaction,
                temporaryName,
                GraphCatalogMetadata.NormalizeNameKey(temporaryName),
                string.Empty,
                DateTimeOffset.UtcNow);
            cancellationToken.ThrowIfCancellationRequested();
            GraphSqliteCatalogWriter.DeleteAllGraphsExcept(connection, transaction, replacement.GraphId);
            GraphSqliteCatalogWriter.UpdateGraph(
                connection,
                transaction,
                replacement.GraphId,
                DefaultGraphName,
                GraphCatalogMetadata.NormalizeNameKey(DefaultGraphName),
                string.Empty,
                DateTimeOffset.UtcNow);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return GraphSqliteCatalogReader.ReadCatalog(connection);
        }
        finally
        {
            writeGate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!isDisposed)
        {
            isDisposed = true;
            writeGate.Dispose();
        }
    }

    /// <inheritdoc />
    public Task<GraphStateSummary> GetStateAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteConnection connection = OpenConnection();
        GraphStateSummary state = GraphSqliteReader.ReadState(connection);
        return Task.FromResult(state);
    }

    /// <inheritdoc />
    public Task<GraphStateSummary> GetStateAsync(
        GraphSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteConnection connection = OpenConnection();
        return Task.FromResult(GraphSqliteReader.ReadState(connection, snapshot));
    }

    /// <inheritdoc />
    public Task<GraphQuerySchema> GetSchemaAsync(
        GraphSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteConnection connection = OpenConnection();
        GraphQuerySchema schema = GraphCypherExecutor.ReadSchema(connection, snapshot, cancellationToken);
        return Task.FromResult(schema);
    }

    /// <inheritdoc />
    public Task<GraphQueryResult> ExecuteOpenCypherAsync(
        GraphQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteConnection connection = OpenConnection();
        GraphQueryResult result = GraphCypherExecutor.Execute(connection, request, cancellationToken);
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<GraphEntityDetails?> GetEntityDetailsAsync(
        GraphEntityKey entity,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteConnection connection = OpenConnection();
        GraphEntityDetails? details = GraphSqliteReader.ReadEntityDetails(connection, entity);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(details);
    }

    /// <inheritdoc />
    public Task<GraphEntityDetails?> GetEntityDetailsAsync(
        GraphSnapshot snapshot,
        GraphEntityKey entity,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteConnection connection = OpenConnection();
        GraphEntityDetails? details = GraphSqliteReader.ReadEntityDetails(connection, snapshot, entity);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(details);
    }

    /// <inheritdoc />
    public Task<GraphRelationshipDetails?> GetRelationshipDetailsAsync(
        GraphRelationshipKey relationship,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteConnection connection = OpenConnection();
        GraphRelationshipDetails? details = GraphSqliteReader.ReadRelationshipDetails(connection, relationship);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(details);
    }

    /// <inheritdoc />
    public Task<GraphRelationshipDetails?> GetRelationshipDetailsAsync(
        GraphSnapshot snapshot,
        GraphRelationshipKey relationship,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteConnection connection = OpenConnection();
        GraphRelationshipDetails? details = GraphSqliteReader.ReadRelationshipDetails(
            connection,
            snapshot,
            relationship);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(details);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<GraphTimelinePoint>> GetTimelineAsync(
        Guid graphId,
        int maximumPoints,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(graphId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPoints);
        ObjectDisposedException.ThrowIf(isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteConnection connection = OpenConnection();
        IReadOnlyList<GraphTimelinePoint> timeline = GraphSqliteReader.ReadTimeline(
            connection,
            graphId,
            maximumPoints);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(timeline);
    }

    /// <inheritdoc />
    public Task<GraphViewport> GetTimelineViewportAsync(
        GraphTimelinePoint point,
        int maximumEntityCount,
        int maximumRelationshipCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(point);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntityCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumEntityCount, MaximumViewportEntityCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRelationshipCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            maximumRelationshipCount,
            MaximumViewportRelationshipCount);
        ObjectDisposedException.ThrowIf(isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteConnection connection = OpenConnection();
        GraphViewport viewport = GraphSqliteReader.ReadTimelineViewport(
            connection,
            point,
            maximumEntityCount,
            maximumRelationshipCount,
            cancellationToken);
        return Task.FromResult(viewport);
    }

    /// <inheritdoc />
    public Task<GraphViewport> GetViewportAsync(
        GraphEntityKey? center,
        int maximumEntityCount,
        int maximumRelationshipCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntityCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumEntityCount, MaximumViewportEntityCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRelationshipCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            maximumRelationshipCount,
            MaximumViewportRelationshipCount);
        ObjectDisposedException.ThrowIf(isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteConnection connection = OpenConnection();
        GraphViewport viewport = GraphSqliteReader.ReadViewport(
            connection,
            center,
            maximumEntityCount,
            maximumRelationshipCount,
            cancellationToken);
        return Task.FromResult(viewport);
    }

    /// <inheritdoc />
    public Task<GraphViewport> GetViewportAsync(
        GraphSnapshot snapshot,
        GraphEntityKey? center,
        int maximumEntityCount,
        int maximumRelationshipCount,
        CancellationToken cancellationToken = default)
    {
        ValidatePositiveViewportLimits(maximumEntityCount, maximumRelationshipCount);
        ObjectDisposedException.ThrowIf(isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteConnection connection = OpenConnection();
        GraphViewport viewport = GraphSqliteReader.ReadViewport(
            connection,
            snapshot,
            center,
            maximumEntityCount,
            maximumRelationshipCount,
            cancellationToken);
        return Task.FromResult(viewport);
    }

    /// <inheritdoc />
    public Task<GraphViewport> GetNeighborhoodAsync(
        IReadOnlyCollection<GraphEntityKey> centers,
        int maximumDepth,
        int maximumEntityCount,
        int maximumRelationshipCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(centers);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDepth);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumDepth, MaximumNeighborhoodDepth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntityCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumEntityCount, MaximumViewportEntityCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRelationshipCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            maximumRelationshipCount,
            MaximumViewportRelationshipCount);
        ObjectDisposedException.ThrowIf(isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteConnection connection = OpenConnection();
        GraphViewport viewport = GraphSqliteReader.ReadNeighborhood(
            connection,
            centers,
            maximumDepth,
            maximumEntityCount,
            maximumRelationshipCount,
            cancellationToken);
        return Task.FromResult(viewport);
    }

    /// <inheritdoc />
    public Task<GraphViewport> GetNeighborhoodAsync(
        GraphSnapshot snapshot,
        IReadOnlyCollection<GraphEntityKey> centers,
        int maximumDepth,
        int maximumEntityCount,
        int maximumRelationshipCount,
        CancellationToken cancellationToken = default)
    {
        ValidateNeighborhoodLimits(centers, maximumDepth, maximumEntityCount, maximumRelationshipCount);
        ObjectDisposedException.ThrowIf(isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteConnection connection = OpenConnection();
        GraphViewport viewport = GraphSqliteReader.ReadNeighborhood(
            connection,
            snapshot,
            centers,
            maximumDepth,
            maximumEntityCount,
            maximumRelationshipCount,
            cancellationToken);
        return Task.FromResult(viewport);
    }

    /// <inheritdoc />
    public async Task<GraphRouteResult> FindRoutesAsync(
        GraphSnapshot snapshot,
        GraphEntityKey start,
        GraphEntityKey destination,
        int maximumEntityCount,
        int maximumRelationshipCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumEntityCount, 2);
        ValidateViewportLimits(maximumEntityCount, maximumRelationshipCount);
        ObjectDisposedException.ThrowIf(isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        return await Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using SqliteConnection connection = OpenConnection();
                return GraphSqliteReader.ReadRoutes(
                    connection,
                    snapshot,
                    start,
                    destination,
                    maximumEntityCount,
                    maximumRelationshipCount,
                    cancellationToken);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<GraphImportResult> ImportAsync(
        IGraphImportSource source,
        GraphImportMode mode,
        CancellationToken cancellationToken = default)
    {
        return await ImportCoreAsync(null, source, mode, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<GraphImportResult> ImportAsync(
        GraphWriteTarget target,
        IGraphImportSource source,
        GraphImportMode mode,
        CancellationToken cancellationToken = default)
    {
        return await ImportCoreAsync(target, source, mode, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<GraphEntityIdentityConflict>> FindIdentityConflictsAsync(
        GraphSnapshot snapshot,
        IEnumerable<GraphEntityIdentityCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ObjectDisposedException.ThrowIf(isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteConnection connection = OpenConnection();
        IReadOnlyList<GraphEntityIdentityConflict> conflicts = GraphSqliteReader.FindIdentityConflicts(
            connection,
            snapshot,
            candidates,
            cancellationToken);
        return Task.FromResult(conflicts);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<GraphEntitySummary>> SearchEntitiesAsync(
        string searchText,
        int maximumResults,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(searchText);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumResults);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumResults, MaximumSearchResults);
        ObjectDisposedException.ThrowIf(isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteConnection connection = OpenConnection();
        IReadOnlyList<GraphEntitySummary> results = GraphSqliteReader.SearchEntities(
            connection,
            searchText,
            maximumResults);
        return Task.FromResult(results);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<GraphEntitySummary>> SearchEntitiesAsync(
        GraphSnapshot snapshot,
        string searchText,
        int maximumResults,
        CancellationToken cancellationToken = default)
    {
        ValidateSearch(searchText, maximumResults);
        ObjectDisposedException.ThrowIf(isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteConnection connection = OpenConnection();
        IReadOnlyList<GraphEntitySummary> results = GraphSqliteReader.SearchEntities(
            connection,
            snapshot,
            searchText,
            maximumResults);
        return Task.FromResult(results);
    }

    private static void ValidateNeighborhoodLimits(
        IReadOnlyCollection<GraphEntityKey> centers,
        int maximumDepth,
        int maximumEntityCount,
        int maximumRelationshipCount)
    {
        ArgumentNullException.ThrowIfNull(centers);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDepth);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumDepth, MaximumNeighborhoodDepth);
        ValidateViewportLimits(maximumEntityCount, maximumRelationshipCount);
    }

    private static void ValidatePositiveViewportLimits(int maximumEntityCount, int maximumRelationshipCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntityCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRelationshipCount);
    }

    private static void ValidateSearch(string searchText, int maximumResults)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(searchText);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumResults);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumResults, MaximumSearchResults);
    }

    private static void ValidateViewportLimits(int maximumEntityCount, int maximumRelationshipCount)
    {
        ValidatePositiveViewportLimits(maximumEntityCount, maximumRelationshipCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumEntityCount, MaximumViewportEntityCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            maximumRelationshipCount,
            MaximumViewportRelationshipCount);
    }

    private static string GetDefaultFilePath()
    {
        string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localApplicationData, "OpenKustoExplorer", "graph.db");
    }

    private static int ImportEntities(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid generationId,
        IEnumerable<GraphEntityObservation> observations,
        CancellationToken cancellationToken,
        out int entitiesObserved)
    {
        int entitiesAdded = 0;
        entitiesObserved = 0;

        foreach (GraphEntityObservation observation in observations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (GraphSqliteWriter.UpsertEntity(
                connection,
                transaction,
                generationId,
                observation.Entity,
                observation.DisplayLabel,
                observation.TemporalInterval.DiscoveredAtUtc))
            {
                entitiesAdded++;
            }

            GraphSqliteWriter.InsertEntityObservation(connection, transaction, generationId, observation);
            entitiesObserved++;
        }

        return entitiesAdded;
    }

    private static int ImportRelationships(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid generationId,
        IEnumerable<GraphRelationshipObservation> observations,
        ref int entitiesAdded,
        CancellationToken cancellationToken,
        out int relationshipsObserved)
    {
        int relationshipsAdded = 0;
        relationshipsObserved = 0;

        foreach (GraphRelationshipObservation observation in observations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DateTimeOffset discoveredAt = observation.TemporalInterval.DiscoveredAtUtc;
            GraphRelationshipKey relationship = observation.Relationship;

            if (GraphSqliteWriter.EnsureEntity(
                connection,
                transaction,
                generationId,
                relationship.Source,
                discoveredAt))
            {
                entitiesAdded++;
            }

            if (GraphSqliteWriter.EnsureEntity(
                connection,
                transaction,
                generationId,
                relationship.Target,
                discoveredAt))
            {
                entitiesAdded++;
            }

            if (GraphSqliteWriter.UpsertRelationship(
                connection,
                transaction,
                generationId,
                relationship,
                discoveredAt))
            {
                relationshipsAdded++;
            }

            GraphSqliteWriter.InsertRelationshipObservation(connection, transaction, generationId, observation);
            relationshipsObserved++;
        }

        return relationshipsAdded;
    }

    private async Task<GraphStateSummary> ClearCoreAsync(
        GraphWriteTarget? requestedTarget,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using SqliteConnection connection = OpenConnection();
            await using SqliteTransaction transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            GraphSnapshot snapshot = requestedTarget is GraphWriteTarget target
                ? target.Snapshot
                : GraphSqliteReader.GetActiveSnapshot(connection, transaction);

            if (requestedTarget is GraphWriteTarget pinnedTarget)
            {
                GraphSqliteCatalogReader.ValidateWriteTarget(connection, transaction, pinnedTarget);
            }

            Guid generationId = GraphSqliteWriter.CreateGeneration(
                connection,
                transaction,
                snapshot.GraphId,
                DateTimeOffset.UtcNow);
            GraphSqliteWriter.ActivateGeneration(connection, transaction, snapshot.GraphId, generationId);
            GraphSqliteCatalogWriter.UpdateGraphTimestamp(
                connection,
                transaction,
                snapshot.GraphId,
                DateTimeOffset.UtcNow);
            cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return GraphSqliteReader.ReadState(
                connection,
                new GraphSnapshot(snapshot.GraphId, generationId));
        }
        finally
        {
            writeGate.Release();
        }
    }

    private async Task<GraphImportResult> ImportCoreAsync(
        GraphWriteTarget? requestedTarget,
        IGraphImportSource source,
        GraphImportMode mode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ObjectDisposedException.ThrowIf(isDisposed, this);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using SqliteConnection connection = OpenConnection();
            await using SqliteTransaction transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            GraphSnapshot snapshot = requestedTarget is GraphWriteTarget target
                ? target.Snapshot
                : GraphSqliteReader.GetActiveSnapshot(connection, transaction);

            if (requestedTarget is GraphWriteTarget pinnedTarget)
            {
                GraphSqliteCatalogReader.ValidateWriteTarget(connection, transaction, pinnedTarget);
            }

            Guid generationId = mode == GraphImportMode.Replace
                ? GraphSqliteWriter.CreateGeneration(
                    connection,
                    transaction,
                    snapshot.GraphId,
                    DateTimeOffset.UtcNow)
                : snapshot.GenerationId;
            GraphSqliteWriter.InsertIngestion(connection, transaction, generationId, source.Ingestion);
            int evidenceCount = 0;

            foreach (GraphEvidence evidence in source.GetEvidence())
            {
                cancellationToken.ThrowIfCancellationRequested();
                GraphSqliteWriter.InsertEvidence(
                    connection,
                    transaction,
                    generationId,
                    source.Ingestion.Id,
                    evidence);
                evidenceCount++;
            }

            int entitiesAdded = ImportEntities(
                connection,
                transaction,
                generationId,
                source.GetEntityObservations(),
                cancellationToken,
                out int entitiesObserved);
            int relationshipsAdded = ImportRelationships(
                connection,
                transaction,
                generationId,
                source.GetRelationshipObservations(),
                ref entitiesAdded,
                cancellationToken,
                out int relationshipsObserved);
            GraphSqliteWriter.UpdateGeneration(
                connection,
                transaction,
                generationId,
                source.Ingestion.CompletedAtUtc);
            GraphSqliteCatalogWriter.UpdateGraphTimestamp(
                connection,
                transaction,
                snapshot.GraphId,
                source.Ingestion.CompletedAtUtc);

            if (mode == GraphImportMode.Replace)
            {
                GraphSqliteWriter.ActivateGeneration(
                    connection,
                    transaction,
                    snapshot.GraphId,
                    generationId);
            }

            cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new GraphImportResult(
                snapshot.GraphId,
                generationId,
                source.Ingestion.Id,
                evidenceCount,
                entitiesAdded,
                entitiesObserved,
                relationshipsAdded,
                relationshipsObserved);
        }
        finally
        {
            writeGate.Release();
        }
    }

    private SqliteConnection OpenConnection()
    {
        SqliteConnection connection = new(connectionString);
        connection.Open();
        using SqliteCommand configurationCommand = connection.CreateCommand();
        configurationCommand.CommandText = """
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 5000;
            """;
        configurationCommand.ExecuteNonQuery();
        return connection;
    }
}
