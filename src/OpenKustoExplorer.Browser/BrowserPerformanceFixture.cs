using System.Globalization;
using System.Text;
using OpenKustoExplorer.Application.Connections;
using OpenKustoExplorer.Application.Documents;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Domain.Schema;
using OpenKustoExplorer.Portable.Graphs;
using OpenKustoExplorer.Portable.Sessions;

namespace OpenKustoExplorer.Browser;

/// <summary>
/// Supplies deterministic in-memory data for loopback Browser performance runs.
/// </summary>
internal sealed class BrowserPerformanceFixture :
    IKustoCatalogService,
    IKustoConnectionStore,
    IKustoDocumentStore,
    IKustoQueryService
{
    private const int ResultRowCount = 5000;
    private const int LargeQueryLineCount = 6000;
    private const string DatabaseName = "PerformanceDatabase";
    private static readonly Uri ClusterUri = new("https://performance.kusto.windows.net");
    private static readonly Guid LargeDocumentId = new("2ad29a2e-f232-4e57-9a6a-afb48b437cb7");
    private readonly KustoDatabaseSchema schema;
    private KustoConnectionCatalog connectionCatalog;
    private KustoDocumentWorkspace documentWorkspace;
    private KustoQueryResult? preparedResult;

    /// <summary>
    /// Initializes a new instance of the <see cref="BrowserPerformanceFixture"/> class.
    /// </summary>
    public BrowserPerformanceFixture()
    {
        schema = new KustoDatabaseSchema(
            ClusterUri.Host,
            DatabaseName,
            [
                new KustoTableSchema(
                    "SyntheticEvents",
                    [
                        new KustoColumnSchema("Timestamp", KustoScalarType.DateTime),
                        new KustoColumnSchema("Region", KustoScalarType.Text),
                        new KustoColumnSchema("Operation", KustoScalarType.Text),
                        new KustoColumnSchema("Status", KustoScalarType.Text),
                        new KustoColumnSchema("DurationMs", KustoScalarType.WideInteger),
                        new KustoColumnSchema("Message", KustoScalarType.Text),
                    ]),
            ]);
        KustoDatabaseConnection database = new(DatabaseName, "Performance database", schema);
        connectionCatalog = new KustoConnectionCatalog(
            [new KustoClusterConnection(ClusterUri, "Performance fixture", [database])]);
        const string QueryText = "SyntheticEvents\n| take 5000";
        Guid documentId = new("2f7cd47c-595c-4cad-9f08-fc69db36eb5d");
        string largeQueryText = CreateLargeQueryText();
        documentWorkspace = new KustoDocumentWorkspace(
            [
                new KustoDocument(
                    documentId,
                    "Performance query",
                    QueryText,
                    QueryText.Length,
                    ClusterUri,
                    DatabaseName),
                new KustoDocument(
                    LargeDocumentId,
                    "Large imported query",
                    largeQueryText,
                    0,
                    ClusterUri,
                    DatabaseName),
            ],
            documentId);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<KustoDatabaseInfo>> GetDatabasesAsync(
        Uri clusterUri,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureTarget(clusterUri, DatabaseName);
        return Task.FromResult<IReadOnlyList<KustoDatabaseInfo>>(
            [new KustoDatabaseInfo(DatabaseName, "Performance database")]);
    }

    /// <inheritdoc />
    public Task<KustoDatabaseSchema> GetDatabaseSchemaAsync(
        Uri clusterUri,
        string databaseName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureTarget(clusterUri, databaseName);
        return Task.FromResult(schema);
    }

    /// <inheritdoc />
    public Task<KustoQueryResult> ExecuteAsync(
        KustoQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureTarget(request.ClusterUri, request.DatabaseName);
        preparedResult ??= CreateResult();
        return Task.FromResult(preparedResult);
    }

    /// <inheritdoc />
    public KustoConnectionCatalog Load() => connectionCatalog;

    /// <inheritdoc />
    KustoDocumentWorkspace IKustoDocumentStore.Load() => documentWorkspace;

    /// <inheritdoc />
    public Task SaveAsync(
        KustoConnectionCatalog catalog,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        cancellationToken.ThrowIfCancellationRequested();
        connectionCatalog = catalog;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    Task IKustoDocumentStore.SaveAsync(
        KustoDocumentWorkspace workspace,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        cancellationToken.ThrowIfCancellationRequested();
        documentWorkspace = workspace;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Creates an empty volatile graph store for a deterministic fixture workspace.
    /// </summary>
    /// <returns>The initialized graph store.</returns>
    internal static Task<JsonGraphStore> CreateGraphStoreAsync()
    {
        return JsonGraphStore.CreateAsync(new EmptyGraphSnapshotStore());
    }

    /// <summary>
    /// Creates an empty volatile recorded-session store for a deterministic fixture workspace.
    /// </summary>
    /// <returns>The initialized recorded-session store.</returns>
    internal static Task<JsonKustoRecordedSessionStore> CreateRecordedSessionStoreAsync()
    {
        return JsonKustoRecordedSessionStore.CreateAsync(new EmptyRecordedSessionSnapshotStore());
    }

    /// <summary>Gets the large imported-query document used by scroll profiling.</summary>
    /// <returns>The deterministic document identifier.</returns>
    internal static Guid GetLargeDocumentId() => LargeDocumentId;

    /// <summary>Creates a deterministic query with the imported-tab line count under test.</summary>
    /// <returns>The large query text.</returns>
    internal static string CreateLargeQueryText()
    {
        StringBuilder text = new();
        for (int index = 0; index < LargeQueryLineCount - 2; index++)
        {
            _ = text.Append("// Imported investigation note ");
            _ = text.Append(index.ToString("D4", CultureInfo.InvariantCulture));
            _ = text.AppendLine();
        }

        _ = text.AppendLine("SyntheticEvents");
        _ = text.Append("| take 5000");
        return text.ToString();
    }

    /// <summary>
    /// Materializes synthetic result data before a measured query action.
    /// </summary>
    /// <returns><see langword="true"/> when the fixture is ready.</returns>
    internal bool Prepare()
    {
        preparedResult ??= CreateResult();
        return true;
    }

    private static KustoQueryResult CreateResult()
    {
        KustoResultColumn[] columns =
        [
            new KustoResultColumn("Timestamp", "datetime"),
            new KustoResultColumn("Region", "string"),
            new KustoResultColumn("Operation", "string"),
            new KustoResultColumn("Status", "string"),
            new KustoResultColumn("DurationMs", "long"),
            new KustoResultColumn("Message", "string"),
        ];
        string[] regions = ["amer", "emea", "apac", "canada"];
        string[] operations = ["ingest", "query", "export", "alert", "retention"];
        DateTimeOffset start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        KustoResultRow[] rows = new KustoResultRow[ResultRowCount];
        for (int index = 0; index < rows.Length; index++)
        {
            string indexText = index.ToString("D5", CultureInfo.InvariantCulture);
            string message = index % 10 == 7
                ? $"needle-7 synthetic event {indexText}"
                : $"routine synthetic event {indexText}";
            rows[index] = new KustoResultRow(
                [
                    start.AddSeconds(index).ToString("O", CultureInfo.InvariantCulture),
                    regions[index % regions.Length],
                    operations[index % operations.Length],
                    index % 17 == 0 ? "Failure" : "Success",
                    (10 + (index % 900)).ToString(CultureInfo.InvariantCulture),
                    message,
                ]);
        }

        return new KustoQueryResult(
            [new KustoResultTable("PrimaryResult", columns, rows)],
            TimeSpan.FromMilliseconds(12));
    }

    private static void EnsureTarget(Uri clusterUri, string databaseName)
    {
        if (clusterUri != ClusterUri || !string.Equals(databaseName, DatabaseName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The Browser performance fixture received an unexpected query target.");
        }
    }

    private sealed class EmptyGraphSnapshotStore : IGraphSnapshotStore
    {
        public Task<string?> LoadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<string?>(null);
        }

        public Task SaveAsync(string snapshotJson, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(snapshotJson);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class EmptyRecordedSessionSnapshotStore : IKustoRecordedSessionSnapshotStore
    {
        public Task<string?> LoadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<string?>(null);
        }

        public Task SaveAsync(string snapshotJson, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(snapshotJson);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
