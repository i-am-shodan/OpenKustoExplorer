using System.Globalization;
using System.Text;
using OpenKustoExplorer.Application.Connections;
using OpenKustoExplorer.Application.Documents;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Application.Sessions;
using OpenKustoExplorer.Domain.Schema;
using OpenKustoExplorer.Graph;
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
    private const string RepairQueryText = """
        OutboundBrowsing
        | summarize dcount(src_ip) by method, bin(timestamp, 1d)
        """;

    private const string TrendQueryText = """
        OutboundBrowsing
        | summarize dcount(src_ip) by method, bin(todatetime(timestamp), 1d)
        """;

    private static readonly Uri ClusterUri = new("https://performance.kusto.windows.net");
    private static readonly Uri IdentityClusterUri = new("https://identity.kusto.windows.net");
    private static readonly Uri OperationsClusterUri = new("https://operations.kusto.windows.net");
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
                new KustoTableSchema(
                    "OutboundBrowsing",
                    [
                        new KustoColumnSchema("timestamp", KustoScalarType.Text),
                        new KustoColumnSchema("url", KustoScalarType.Text),
                        new KustoColumnSchema("src_ip", KustoScalarType.Text),
                        new KustoColumnSchema("method", KustoScalarType.Text),
                    ]),
                new KustoTableSchema(
                    "AuthenticationEvents",
                    [
                        new KustoColumnSchema("src_ip", KustoScalarType.Text),
                        new KustoColumnSchema("username", KustoScalarType.Text),
                        new KustoColumnSchema("risk", KustoScalarType.Text),
                    ]),
                new KustoTableSchema(
                    "Employees",
                    [
                        new KustoColumnSchema("username", KustoScalarType.Text),
                        new KustoColumnSchema("email_addr", KustoScalarType.Text),
                        new KustoColumnSchema("hostname", KustoScalarType.Text),
                    ]),
                new KustoTableSchema(
                    "Email",
                    [
                        new KustoColumnSchema("recipient", KustoScalarType.Text),
                        new KustoColumnSchema("sender", KustoScalarType.Text),
                        new KustoColumnSchema("subject", KustoScalarType.Text),
                    ]),
            ]);
        KustoDatabaseConnection database = new(DatabaseName, "Performance database", schema);
        connectionCatalog = new KustoConnectionCatalog(
            [
                new KustoClusterConnection(ClusterUri, "Security operations", [database]),
                new KustoClusterConnection(IdentityClusterUri, "Identity analytics", []),
                new KustoClusterConnection(OperationsClusterUri, "Service reliability", []),
            ]);
        Guid documentId = new("2f7cd47c-595c-4cad-9f08-fc69db36eb5d");
        string largeQueryText = CreateLargeQueryText();
        documentWorkspace = new KustoDocumentWorkspace(
            [
                new KustoDocument(
                    documentId,
                    "Alert timeline",
                    TrendQueryText,
                    TrendQueryText.Length,
                    ClusterUri,
                    DatabaseName,
                    KustoDocumentTabColor.Teal,
                    "INC-2026-0917"),
                new KustoDocument(
                    new Guid("52457fc8-d28e-4a4e-97c6-da6b6fa654ac"),
                    "Identity pivots",
                    "SyntheticEvents\n| where Operation == 'alert'\n| summarize Events=count() by Region, Status",
                    0,
                    ClusterUri,
                    DatabaseName,
                    KustoDocumentTabColor.Blue,
                    "INC-2026-0917"),
                new KustoDocument(
                    new Guid("9cb9f9a4-78a1-4ec3-919e-5fbb14e3da5e"),
                    "Endpoint activity",
                    "SyntheticEvents\n| where Status == 'Failure'\n| project Timestamp, Region, Operation, DurationMs",
                    0,
                    ClusterUri,
                    DatabaseName,
                    KustoDocumentTabColor.Green,
                    "INC-2026-0917"),
                new KustoDocument(
                    new Guid("86535049-2087-4557-8fe7-71285bd3287f"),
                    "Service baselines",
                    "SyntheticEvents\n| summarize p95(DurationMs) by Operation",
                    0,
                    ClusterUri,
                    DatabaseName,
                    KustoDocumentTabColor.Orange,
                    "REFERENCE"),
                new KustoDocument(
                    LargeDocumentId,
                    "Imported investigation",
                    largeQueryText,
                    0,
                    ClusterUri,
                    DatabaseName,
                    KustoDocumentTabColor.Purple,
                    "REFERENCE"),
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
        if (request.QueryText.Contains("OutboundBrowsing", StringComparison.OrdinalIgnoreCase))
        {
            if (!request.QueryText.Contains("todatetime(timestamp)", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Semantic error: bin(): argument #1 must be a datetime value. [line:position=2:47]");
            }

            return Task.FromResult(CreateOutboundBrowsingTrendResult());
        }

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
    internal static async Task<JsonGraphStore> CreateGraphStoreAsync()
    {
        JsonGraphStore store = await JsonGraphStore
            .CreateAsync(new EmptyGraphSnapshotStore())
            .ConfigureAwait(false);
        await store.ImportAsync(CreateInvestigationGraphBatch(), GraphImportMode.Add).ConfigureAwait(false);
        return store;
    }

    /// <summary>
    /// Creates an empty volatile recorded-session store for a deterministic fixture workspace.
    /// </summary>
    /// <returns>The initialized recorded-session store.</returns>
    internal static async Task<JsonKustoRecordedSessionStore> CreateRecordedSessionStoreAsync()
    {
        JsonKustoRecordedSessionStore store = await JsonKustoRecordedSessionStore
            .CreateAsync(new EmptyRecordedSessionSnapshotStore())
            .ConfigureAwait(false);
        await SeedRecordedSessionAsync(store).ConfigureAwait(false);
        return store;
    }

    /// <summary>Gets the large imported-query document used by scroll profiling.</summary>
    /// <returns>The deterministic document identifier.</returns>
    internal static Guid GetLargeDocumentId() => LargeDocumentId;

    /// <summary>Gets the intentionally invalid query used by the Copilot repair capture.</summary>
    /// <returns>The query text whose string timestamp requires an explicit conversion.</returns>
    internal static string GetRepairQueryText() => RepairQueryText;

    /// <summary>Gets the corrected security trend used by the workbench capture.</summary>
    /// <returns>The query text that visualizes unique outbound source addresses over time.</returns>
    internal static string GetTrendQueryText() => TrendQueryText;

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

    private static KustoQueryResult CreateOutboundBrowsingTrendResult()
    {
        KustoResultColumn[] columns =
        [
            new KustoResultColumn("timestamp", "datetime"),
            new KustoResultColumn("method", "string"),
            new KustoResultColumn("dcount_src_ip", "long"),
        ];
        string[] methods = ["GET", "POST"];
        DateTimeOffset start = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        KustoResultRow[] rows = Enumerable.Range(0, 16)
            .Select(index =>
            {
                int dayIndex = index / methods.Length;
                return new KustoResultRow(
                    [
                        start.AddDays(dayIndex).ToString("O", CultureInfo.InvariantCulture),
                        methods[index % methods.Length],
                        (18 + ((index * 11) % 37)).ToString(CultureInfo.InvariantCulture),
                    ]);
            })
            .ToArray();
        return new KustoQueryResult(
            [new KustoResultTable("PrimaryResult", columns, rows)],
            TimeSpan.FromMilliseconds(84));
    }

    private static GraphImportBatch CreateInvestigationGraphBatch()
    {
        DateTimeOffset observedAt = new(2026, 9, 10, 9, 30, 0, TimeSpan.Zero);
        GraphEvidence evidence = new(
            "c2-investigation-row",
            "c2-investigation-hash",
            "PrimaryResult",
            0,
            "{\"columns\":[\"source\",\"target\",\"relationship\"]}",
            "{\"incident\":\"Malware C2 investigation\"}");
        GraphTemporalInterval interval = new(observedAt);
        (GraphEntityKey Key, string Label, string SourceLabel)[] entities =
        [
            (new GraphEntityKey(GraphEntityKind.User, "User", "alice.chen"), "Alice Chen", "IdentityInfo"),
            (new GraphEntityKey(GraphEntityKind.Device, "Device", "WKSTN-042"), "Workstation 042", "DeviceInfo"),
            (new GraphEntityKey(GraphEntityKind.IpAddress, "IpAddress", "198.51.100.42"), "198.51.100.42", "NetworkSession"),
            (new GraphEntityKey(GraphEntityKind.Url, "Url", "update-check.example.test"), "update-check.example.test", "OutboundBrowsing"),
            (new GraphEntityKey(GraphEntityKind.Application, "Application", "finance-api"), "Finance API", "CloudApplication"),
            (new GraphEntityKey(GraphEntityKind.ServicePrincipal, "ServicePrincipal", "expense-api"), "Expense API identity", "ServicePrincipal"),
            (new GraphEntityKey(GraphEntityKind.Group, "Group", "finance-analysts"), "Finance Analysts", "GroupMembership"),
            (new GraphEntityKey(GraphEntityKind.Database, "Database", "finance-warehouse"), "Finance Warehouse", "DataResource"),
        ];
        (int Source, int Target, string Type)[] relationships =
        [
            (0, 1, "SIGNED_IN_TO"),
            (1, 2, "CONNECTED_FROM"),
            (2, 3, "REACHED"),
            (3, 4, "CALLED"),
            (4, 5, "USES_IDENTITY"),
            (5, 7, "ACCESSED"),
            (0, 6, "MEMBER_OF"),
            (6, 7, "CAN_READ"),
        ];
        GraphEntityObservation[] entityObservations = entities
            .Select(entity => new GraphEntityObservation(
                Guid.NewGuid(),
                entity.Key,
                entity.Label,
                [entity.SourceLabel],
                new Dictionary<string, string>(StringComparer.Ordinal),
                interval,
                [evidence.OccurrenceId]))
            .ToArray();
        GraphRelationshipObservation[] relationshipObservations = relationships
            .Select(relationship => new GraphRelationshipObservation(
                Guid.NewGuid(),
                new GraphRelationshipKey(
                    entities[relationship.Source].Key,
                    entities[relationship.Target].Key,
                    relationship.Type),
                [relationship.Type],
                new Dictionary<string, string>(StringComparer.Ordinal),
                interval,
                [evidence.OccurrenceId]))
            .ToArray();
        return new GraphImportBatch(
            new GraphIngestion(
                Guid.NewGuid(),
                GraphIngestionSourceKind.ManualQuery,
                Guid.NewGuid(),
                "Malware C2 investigation",
                ClusterUri,
                DatabaseName,
                "OutboundBrowsing | make-graph src_ip --> url",
                observedAt.AddSeconds(-1),
                observedAt),
            [evidence],
            entityObservations,
            relationshipObservations);
    }

    private static async Task SeedRecordedSessionAsync(JsonKustoRecordedSessionStore store)
    {
        const string InvestigationName = "Malware C2 investigation";
        const string IpAddress = "198.51.100.42";
        const string Username = "alice.chen";
        const string EmailAddress = "alice.chen@example.test";
        const string Hostname = "WKSTN-042";
        string maliciousUrl = new UriBuilder(Uri.UriSchemeHttps, "update-check.example.test")
        {
            Path = "session",
        }.Uri.AbsoluteUri.TrimEnd('/');
        DateTimeOffset startedAtUtc = new(2026, 9, 10, 9, 30, 0, TimeSpan.Zero);
        KustoDatabaseSchema investigationSchema = new(
            ClusterUri.Host,
            DatabaseName,
            [
                new KustoTableSchema(
                    "OutboundBrowsing",
                    [
                        new KustoColumnSchema("url", KustoScalarType.Text),
                        new KustoColumnSchema("src_ip", KustoScalarType.Text),
                    ]),
                new KustoTableSchema(
                    "AuthenticationEvents",
                    [
                        new KustoColumnSchema("src_ip", KustoScalarType.Text),
                        new KustoColumnSchema("username", KustoScalarType.Text),
                        new KustoColumnSchema("risk", KustoScalarType.Text),
                    ]),
                new KustoTableSchema(
                    "Employees",
                    [
                        new KustoColumnSchema("username", KustoScalarType.Text),
                        new KustoColumnSchema("email_addr", KustoScalarType.Text),
                        new KustoColumnSchema("hostname", KustoScalarType.Text),
                    ]),
                new KustoTableSchema(
                    "Email",
                    [
                        new KustoColumnSchema("recipient", KustoScalarType.Text),
                        new KustoColumnSchema("sender", KustoScalarType.Text),
                        new KustoColumnSchema("subject", KustoScalarType.Text),
                    ]),
            ]);
        KustoPredicateInterestExtractor interestExtractor = new();
        KustoRecordedRelationExtractor relationExtractor = new();
        KustoRecordingPeriod period = await store
            .CreateSessionAsync(InvestigationName, startedAtUtc)
            .ConfigureAwait(false);

        Guid browsingExecution = await RecordAsync(
            "Flagged outbound browsing",
            $"OutboundBrowsing | where url == \"{maliciousUrl}\" or src_ip == \"{IpAddress}\" | take 7",
            CreateInvestigationTable(
                ["timestamp", "url", "src_ip", "method", "verdict"],
                [
                    ["2026-09-10T09:27:12Z", maliciousUrl, IpAddress, "POST", "blocked"],
                    ["2026-09-10T09:27:45Z", "https://login.example.test", IpAddress, "GET", "allowed"],
                    ["2026-09-10T09:28:03Z", maliciousUrl, "198.51.100.73", "POST", "blocked"],
                    ["2026-09-10T09:28:39Z", "https://cdn.example.test", IpAddress, "GET", "allowed"],
                    ["2026-09-10T09:29:10Z", maliciousUrl, IpAddress, "POST", "blocked"],
                    ["2026-09-10T09:29:31Z", "https://portal.example.test", "198.51.100.18", "GET", "allowed"],
                    ["2026-09-10T09:29:58Z", maliciousUrl, IpAddress, "POST", "blocked"],
                ]),
            startedAtUtc).ConfigureAwait(false);
        Guid authenticationExecution = await RecordAsync(
            "Source IP to risky sign-in",
            $"AuthenticationEvents | where src_ip == \"{IpAddress}\"",
            CreateInvestigationTable(["src_ip", "username", "risk"], [[IpAddress, Username, "high"]]),
            startedAtUtc.AddMinutes(1)).ConfigureAwait(false);
        Guid identityExecution = await RecordAsync(
            "Identity to employee record",
            $"Employees | where username == \"{Username}\"",
            CreateInvestigationTable(
                ["username", "email_addr", "hostname"],
                [[Username, EmailAddress, Hostname]]),
            startedAtUtc.AddMinutes(2)).ConfigureAwait(false);
        _ = await RecordAsync(
            "Mailbox activity check",
            $"Email | where recipient == \"{EmailAddress}\"",
            CreateInvestigationTable(
                ["recipient", "sender", "subject"],
                [[EmailAddress, "alerts@example.test", "Unusual sign-in detected"]]),
            startedAtUtc.AddMinutes(3)).ConfigureAwait(false);
        Guid hostExecution = await RecordAsync(
            "Email to managed host",
            $"Employees | where email_addr == \"{EmailAddress}\"",
            CreateInvestigationTable(
                ["email_addr", "hostname"],
                [[EmailAddress, Hostname]]),
            startedAtUtc.AddMinutes(4)).ConfigureAwait(false);

        KustoRecordedValueCoordinate urlCoordinate = new(browsingExecution, 0, 0, 1);
        KustoRecordedValueCoordinate ipCoordinate = new(authenticationExecution, 0, 0, 0);
        KustoRecordedValueCoordinate usernameCoordinate = new(identityExecution, 0, 0, 0);
        KustoRecordedValueCoordinate hostCoordinate = new(hostExecution, 0, 0, 1);
        KustoRecordedValueCoordinate blockedIpCoordinate = new(browsingExecution, 0, 0, 2);
        KustoRecordedValueCoordinate blockedUrlCoordinate = new(browsingExecution, 0, 2, 1);
        KustoRecordedValueCoordinate repeatedIpCoordinate = new(browsingExecution, 0, 4, 2);
        await store.AddMarksAsync(
            period.SessionId,
            KustoRecordedMarkKind.Cell,
            [blockedIpCoordinate, blockedUrlCoordinate, repeatedIpCoordinate, ipCoordinate, usernameCoordinate],
            startedAtUtc.AddMinutes(5)).ConfigureAwait(false);
        await store.SetEndpointAsync(
            period.SessionId,
            KustoChainEndpointRole.Start,
            urlCoordinate).ConfigureAwait(false);
        await store.SetEndpointAsync(
            period.SessionId,
            KustoChainEndpointRole.End,
            hostCoordinate).ConfigureAwait(false);
        await store.StopRecordingAsync(period.Id, startedAtUtc.AddMinutes(6)).ConfigureAwait(false);

        async Task<Guid> RecordAsync(
            string title,
            string queryText,
            KustoResultTable table,
            DateTimeOffset queryStartedAtUtc)
        {
            IReadOnlyList<KustoPredicateInterest> interests = interestExtractor.Extract(
                queryText,
                investigationSchema);
            KustoRecordedRelationDescriptor? relation = relationExtractor.Extract(
                queryText,
                investigationSchema);
            Guid executionId = await store.BeginExecutionAsync(new KustoRecordedExecutionStart(
                period.Id,
                Guid.NewGuid(),
                "Security investigation",
                new KustoQueryRequest(ClusterUri, DatabaseName, queryText),
                queryStartedAtUtc,
                interests,
                relation)).ConfigureAwait(false);
            await store.CompleteExecutionAsync(
                executionId,
                new KustoRecordedExecutionCompletion(
                    KustoRecordedExecutionStatus.Succeeded,
                    queryStartedAtUtc.AddSeconds(1),
                    new KustoQueryResult([table], TimeSpan.FromSeconds(1)),
                    null)).ConfigureAwait(false);
            await store.RenameExecutionAsync(executionId, title).ConfigureAwait(false);
            return executionId;
        }
    }

    private static KustoResultTable CreateInvestigationTable(
        IReadOnlyList<string> columnNames,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        KustoResultColumn[] columns = columnNames
            .Select(columnName => new KustoResultColumn(columnName, "string"))
            .ToArray();
        KustoResultRow[] resultRows = rows
            .Select(row => new KustoResultRow(row))
            .ToArray();
        return new KustoResultTable("PrimaryResult", columns, resultRows);
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
