using System.ComponentModel;
using GitHub.Copilot;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;
using OpenKustoExplorer.Application.Dashboards;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Graphs;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Domain.Schema;
using OpenKustoExplorer.Graph;
using OpenKustoExplorer.Graph.Query;
using OpenKustoExplorer.Infrastructure.Assistance;
using OpenKustoExplorer.Infrastructure.Dashboards;
using OpenKustoExplorer.Infrastructure.Graph;
using OpenKustoExplorer.Infrastructure.Language;
using SkiaSharp;

namespace OpenKustoExplorer.AotSmoke;

/// <summary>
/// Provides the entry point for published Native AOT smoke validation.
/// </summary>
internal static class Program
{
    private static int Main()
    {
        RunKustoLanguageSmokeTest();
        RunDashboardStoreSmokeTest();
        RunSqliteSmokeTest();
        RunGraphStoreSmokeTest();
        RunGraphIngestionSmokeTest();
        RunMsaglSmokeTest();
        RunSkiaSmokeTest();
        RunCopilotToolSmokeTest();
        Console.WriteLine("Open Kusto Explorer Native AOT smoke tests passed.");

        int exitCode = 0;
        return exitCode;
    }

    private static void RunCopilotToolSmokeTest()
    {
        AIFunction tool = CopilotTool.DefineTool(
            ([Description("Graph node identifier")] string nodeId) => nodeId,
            factoryOptions: new AIFunctionFactoryOptions
            {
                Name = "lookup_graph_node",
                Description = "Looks up one graph node without changing the graph.",
            });

        if (!string.Equals(tool.Name, "lookup_graph_node", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("GitHub Copilot custom-tool construction failed.");
        }
    }

    private static void RunDashboardStoreSmokeTest()
    {
        string directoryPath = Path.Combine(Path.GetTempPath(), $"OpenKustoExplorer-AotDashboard-{Guid.NewGuid():N}");
        string filePath = Path.Combine(directoryPath, "dashboards.json");

        try
        {
            FileKustoDashboardStore store = new(filePath);
            KustoDashboardWidget widget = new(
                Guid.NewGuid(),
                "Native AOT table",
                new UriBuilder(Uri.UriSchemeHttps, "cluster.example.com").Uri,
                "Telemetry",
                "Events | take 10",
                TimeSpan.FromMinutes(5),
                KustoDashboardWidgetDisplayMode.Table,
                KustoVisualizationKind.Table,
                new KustoDashboardWidgetLayout(0, 0, 16, 11),
                "#FFFFFF",
                "#1F2933",
                "#167D8D");
            KustoDashboard dashboard = new(
                Guid.NewGuid(),
                "Native AOT dashboard",
                "#EEF2F3",
                [widget]);
            store.Save(new KustoDashboardCatalog([dashboard]));
            KustoDashboard restored = store.Load().Dashboards[0];
            using MemoryStream stream = new();
            store.Export(stream, restored);
            stream.Position = 0;
            KustoDashboard imported = store.Import(stream);

            if (imported.Id != dashboard.Id
                || imported.Widgets.Count != 1
                || imported.Widgets[0].Layout.ColumnSpan != 16)
            {
                throw new InvalidOperationException("Dashboard persistence failed in the published executable.");
            }
        }
        finally
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, true);
            }
        }
    }

    private static void RunKustoLanguageSmokeTest()
    {
        const string query = "StormEvents | project State";
        const string graphQuery = "datatable(source:string, target:string)[\"a\", \"b\"] | make-graph source --> target";
        KustoColumnSchema stateColumn = new("State", KustoScalarType.Text);
        KustoTableSchema stormEventsTable = new("StormEvents", [stateColumn]);
        KustoDatabaseSchema databaseSchema = new(
            "help.kusto.windows.net",
            "Samples",
            [stormEventsTable]);
        KustoLanguageService languageService = new();

        KustoLanguageAnalysis analysis = languageService.Analyze(query, query.Length, databaseSchema);
        KustoGraphQueryPlan? graphPlan = languageService.GetGraphQueryPlanAtPosition(
            graphQuery,
            graphQuery.Length);
        bool hasKnownTableClassification = analysis.Classifications.Any(
            classification => IsKnownTableClassification(query, classification));

        if (!hasKnownTableClassification
            || analysis.Diagnostics.Count > 0
            || graphPlan is null
            || !graphPlan.ExportQueryText.Contains("graph-to-table", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Kusto language analysis or graph planning failed in the published executable.");
        }
    }

    private static void RunGraphStoreSmokeTest()
    {
        string directoryPath = Path.Combine(Path.GetTempPath(), $"OpenKustoExplorer-AotSmoke-{Guid.NewGuid():N}");
        string filePath = Path.Combine(directoryPath, "graph.db");
        DateTimeOffset completedAtUtc = DateTimeOffset.UtcNow;

        try
        {
            using SqliteGraphStore store = new(filePath);
            GraphEvidence evidence = new(
                "native-aot-occurrence",
                "native-aot-content-hash",
                "GraphNodes",
                0,
                "{\"columns\":[\"id\"]}",
                "{\"id\":\"native-aot-user\"}");
            GraphEntityObservation observation = new(
                Guid.NewGuid(),
                new GraphEntityKey(GraphEntityKind.User, "User", "native-aot-user"),
                "Native AOT User",
                ["User"],
                new Dictionary<string, string>(StringComparer.Ordinal),
                new GraphTemporalInterval(completedAtUtc),
                [evidence.OccurrenceId]);
            Uri clusterUri = new UriBuilder(Uri.UriSchemeHttps, "cluster.example.com").Uri;
            GraphIngestion ingestion = new(
                Guid.NewGuid(),
                GraphIngestionSourceKind.ManualQuery,
                Guid.NewGuid(),
                "Native AOT graph smoke",
                clusterUri,
                "Security",
                "GraphNodes | make-graph source --> target",
                completedAtUtc.AddSeconds(-1),
                completedAtUtc);
            GraphImportBatch batch = new(ingestion, [evidence], [observation], []);

            store.ImportAsync(batch, GraphImportMode.Add).GetAwaiter().GetResult();
            GraphStateSummary importedState = store.GetStateAsync().GetAwaiter().GetResult();
            IReadOnlyList<GraphEntitySummary> matches = store
                .SearchEntitiesAsync("native", 10)
                .GetAwaiter()
                .GetResult();
            GraphQueryResult cypherResult = store.ExecuteOpenCypherAsync(new GraphQueryRequest(
                importedState.Snapshot,
                "MATCH (n:User) RETURN n.canonicalId AS id"))
                .GetAwaiter()
                .GetResult();
            IReadOnlyList<AIFunction> graphTools = CopilotGraphTools.Create(
                store,
                store,
                importedState.Snapshot);
            GraphStateSummary secondGraph = store.CreateGraphAsync(
                "Native AOT second graph",
                "Catalog isolation probe")
                .GetAwaiter()
                .GetResult();
            GraphCatalog catalog = store.GetCatalogAsync().GetAwaiter().GetResult();
            store.ActivateGraphAsync(importedState.GraphId).GetAwaiter().GetResult();
            GraphStateSummary clearedState = store.ClearAsync(new GraphWriteTarget(importedState.Snapshot))
                .GetAwaiter()
                .GetResult();

            if (matches.Count != 1
                || !string.Equals(matches[0].DisplayLabel, "Native AOT User", StringComparison.Ordinal)
                || !cypherResult.Succeeded
                || !string.Equals(cypherResult.Rows[0].Values[0].DisplayText, "native-aot-user", StringComparison.Ordinal)
                || graphTools.Count != 4
                || graphTools.All(tool => !string.Equals(
                    tool.Name,
                    CopilotGraphTools.QueryToolName,
                    StringComparison.Ordinal))
                || graphTools.All(tool => !string.Equals(
                    tool.Name,
                    CopilotGraphTools.RouteToolName,
                    StringComparison.Ordinal))
                || catalog.Graphs.Count != 2
                || catalog.ActiveGraphId != secondGraph.GraphId
                || clearedState.GraphId != importedState.GraphId
                || !clearedState.IsEmpty)
            {
                throw new InvalidOperationException("Durable graph store operations failed in the published executable.");
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();

            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, true);
            }
        }
    }

    private static void RunGraphIngestionSmokeTest()
    {
        const string Query = "graph(\"SecurityGraph\")";
        string directoryPath = Path.Combine(Path.GetTempPath(), $"OpenKustoExplorer-AotIngestion-{Guid.NewGuid():N}");
        string filePath = Path.Combine(directoryPath, "graph.db");

        try
        {
            using SqliteGraphStore store = new(filePath);
            KustoGraphQueryPlan plan = new(
                new KustoQuerySelection(Query, 0, Query.Length),
                KustoGraphSourceKind.GraphFunction,
                "export query",
                "nodes_export",
                "edges_export",
                "node_hash",
                "source_hash",
                "target_hash",
                null,
                null);
            Uri clusterUri = new UriBuilder(Uri.UriSchemeHttps, "cluster.example.com").Uri;
            KustoGraphIngestionRequest request = new(
                new KustoQueryRequest(clusterUri, "Security", Query),
                plan,
                GraphImportMode.Add,
                GraphIngestionSourceKind.ManualQuery,
                Guid.NewGuid(),
                "Native AOT staged ingestion");
            KustoGraphIngestionService service = new(new NativeGraphQueryService(), store);

            KustoGraphIngestionResult result = service.ExecuteAsync(request).GetAwaiter().GetResult();
            GraphStateSummary state = store.GetStateAsync().GetAwaiter().GetResult();

            if (result.Import.EvidenceCount != 3
                || state.EntityCount != 2
                || state.RelationshipCount != 1)
            {
                throw new InvalidOperationException("Staged graph ingestion failed in the published executable.");
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();

            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, true);
            }
        }
    }

    private static bool IsKnownTableClassification(
        string query,
        KustoClassification classification)
    {
        string classifiedText = query.Substring(classification.Start, classification.Length);
        bool isKnownTable = classifiedText.Equals("StormEvents", StringComparison.Ordinal)
            && classification.Kind.Equals("Table", StringComparison.Ordinal);

        return isKnownTable;
    }

    private static void RunMsaglSmokeTest()
    {
        const int NodeCount = 12;
        DateTimeOffset discoveredAt = new(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);
        List<GraphEntitySummary> entities = [];
        List<GraphRelationshipKey> relationships = [];

        for (int index = 0; index < NodeCount; index++)
        {
            GraphEntityKey entity = new(GraphEntityKind.Host, "Host", $"native-aot-host-{index:N0}");
            entities.Add(new GraphEntitySummary(
                entity,
                $"Native AOT Host {index:N0}",
                discoveredAt,
                discoveredAt,
                index is 0 or NodeCount - 1 ? 1 : 2));

            if (index > 0)
            {
                relationships.Add(new GraphRelationshipKey(
                    entities[index - 1].Entity,
                    entity,
                    "ConnectedTo"));
            }
        }

        GraphViewport viewport = new(entities[0].Entity, entities, relationships, false);
        MsaglGraphLayoutService service = new();
        GraphLayout layout = service.LayoutAsync(viewport).GetAwaiter().GetResult();

        if (layout.Nodes.Count != NodeCount
            || layout.Edges.Count != NodeCount - 1
            || layout.Width <= 0
            || layout.Height <= 0
            || layout.Edges.Any(edge => edge.Route.Count < 2))
        {
            throw new InvalidOperationException("The production MSAGL pipeline did not produce usable graph geometry.");
        }
    }

    private static void RunSkiaSmokeTest()
    {
        using SKSurface surface = SKSurface.Create(new SKImageInfo(32, 32))
            ?? throw new InvalidOperationException("Skia could not create a drawing surface.");
        using SKPaint paint = new()
        {
            Color = SKColors.CornflowerBlue,
            IsAntialias = true,
        };
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawCircle(16, 16, 8, paint);
        using SKImage image = surface.Snapshot();
        using SKData encoded = image.Encode(SKEncodedImageFormat.Png, 100);

        if (encoded.Size == 0)
        {
            throw new InvalidOperationException("Skia did not encode the rendered graph probe.");
        }
    }

    private static void RunSqliteSmokeTest()
    {
        using SqliteConnection connection = new("Data Source=:memory:");
        connection.Open();
        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand setup = connection.CreateCommand();
        setup.Transaction = transaction;
        setup.CommandText = """
            CREATE TABLE graph_nodes (id TEXT PRIMARY KEY, label TEXT NOT NULL);
            CREATE VIRTUAL TABLE graph_nodes_search USING fts5(id, label);
            INSERT INTO graph_nodes VALUES ('node-1', 'Crown jewel database');
            INSERT INTO graph_nodes_search VALUES ('node-1', 'Crown jewel database');
            """;
        setup.ExecuteNonQuery();
        transaction.Commit();

        using SqliteCommand search = connection.CreateCommand();
        search.CommandText = "SELECT id FROM graph_nodes_search WHERE graph_nodes_search MATCH 'crown';";
        string? nodeId = search.ExecuteScalar() as string;

        if (!string.Equals(nodeId, "node-1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("SQLite graph full-text search failed.");
        }
    }

    private sealed class NativeGraphQueryService : IKustoGraphQueryService
    {
        Task<KustoGraphExportSummary> IKustoGraphQueryService.ExecuteGraphAsync(
            KustoQueryRequest request,
            KustoGraphQueryPlan plan,
            IKustoGraphExportSink sink,
            CancellationToken cancellationToken)
        {
            _ = request;
            cancellationToken.ThrowIfCancellationRequested();
            KustoResultColumn[] nodeColumns =
            [
                new("node_hash", "long"),
                new("id", "string"),
                new("name", "string"),
                new("type", "string"),
            ];
            sink.BeginTable(KustoGraphExportTableKind.Nodes, plan.NodeTableName, nodeColumns);
            sink.WriteRow(KustoGraphExportTableKind.Nodes, ["1", "native-user", "Native User", "User"]);
            sink.WriteRow(KustoGraphExportTableKind.Nodes, ["2", "native-host", "Native Host", "Host"]);
            sink.EndTable(KustoGraphExportTableKind.Nodes);
            KustoResultColumn[] edgeColumns =
            [
                new("source_hash", "long"),
                new("target_hash", "long"),
                new("edge_type", "string"),
            ];
            sink.BeginTable(KustoGraphExportTableKind.Edges, plan.EdgeTableName, edgeColumns);
            sink.WriteRow(KustoGraphExportTableKind.Edges, ["1", "2", "ConnectedTo"]);
            sink.EndTable(KustoGraphExportTableKind.Edges);

            return Task.FromResult(new KustoGraphExportSummary(2, 1, TimeSpan.FromMilliseconds(1)));
        }
    }
}
