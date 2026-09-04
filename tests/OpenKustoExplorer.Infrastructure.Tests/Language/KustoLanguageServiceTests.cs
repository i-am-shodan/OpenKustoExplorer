using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Domain.Schema;
using OpenKustoExplorer.Infrastructure.Language;

namespace OpenKustoExplorer.Infrastructure.Tests.Language;

/// <summary>
/// Verifies the official Kusto language engine through the application-owned adapter contract.
/// </summary>
public sealed class KustoLanguageServiceTests
{
    /// <summary>
    /// Verifies that the active database tables appear at the start of a document.
    /// </summary>
    [Fact]
    public void AnalyzeAtDocumentStartIncludesKnownTableCompletion()
    {
        KustoLanguageService languageService = new();

        KustoLanguageAnalysis analysis = languageService.Analyze(string.Empty, 0, CreateDatabaseSchema());

        Assert.Contains(
            analysis.Completions,
            completion => completion.DisplayText.Equals("StormEvents", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that stored database functions participate in schema-aware completion.
    /// </summary>
    [Fact]
    public void AnalyzeAtDocumentStartIncludesStoredFunctionCompletion()
    {
        KustoFunctionSchema function = new(
            "RecentStorms",
            "(lookback: timespan)",
            "{ StormEvents | where StartTime > ago(lookback) }",
            "Samples",
            "Returns recent storm events");
        KustoDatabaseSchema schema = new(
            "help.kusto.windows.net",
            "Samples",
            CreateDatabaseSchema().Tables,
            [function]);
        KustoLanguageService languageService = new();

        KustoLanguageAnalysis analysis = languageService.Analyze(string.Empty, 0, schema);

        Assert.Contains(
            analysis.Completions,
            completion => completion.DisplayText.StartsWith("RecentStorms", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that columns from the pipeline input table appear in expression completions.
    /// </summary>
    [Fact]
    public void AnalyzeAfterWhereOperatorIncludesKnownColumnCompletion()
    {
        const string query = "StormEvents\n| where ";
        KustoLanguageService languageService = new();

        KustoLanguageAnalysis analysis = languageService.Analyze(query, query.Length, CreateDatabaseSchema());

        Assert.Contains(
            analysis.Completions,
            completion => completion.DisplayText.Equals("State", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that semantic classification distinguishes known table references and query operators.
    /// </summary>
    [Fact]
    public void AnalyzeClassifiesKnownTableAndQueryOperator()
    {
        const string query = "StormEvents | take 10";
        KustoLanguageService languageService = new();

        KustoLanguageAnalysis analysis = languageService.Analyze(query, query.Length, CreateDatabaseSchema());

        Assert.Contains(
            analysis.Classifications,
            classification => IsClassification(query, classification, "StormEvents", "Table"));
        Assert.Contains(
            analysis.Classifications,
            classification => IsClassification(query, classification, "take", "QueryOperator"));
    }

    /// <summary>
    /// Verifies that semantic analysis reports a reference to an unknown column.
    /// </summary>
    [Fact]
    public void AnalyzeReportsUnknownColumnDiagnostic()
    {
        const string query = "StormEvents | project MissingColumn";
        KustoLanguageService languageService = new();

        KustoLanguageAnalysis analysis = languageService.Analyze(query, query.Length, CreateDatabaseSchema());

        Assert.Contains(
            analysis.Diagnostics,
            diagnostic => diagnostic.Message.Contains("MissingColumn", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that semantic analysis accepts a known table and column without diagnostics.
    /// </summary>
    [Fact]
    public void AnalyzeAcceptsKnownTableAndColumn()
    {
        const string query = "StormEvents | project State";
        KustoLanguageService languageService = new();

        KustoLanguageAnalysis analysis = languageService.Analyze(query, query.Length, CreateDatabaseSchema());

        Assert.Empty(analysis.Diagnostics);
    }

    /// <summary>
    /// Verifies that an invalid caret position is rejected before parsing begins.
    /// </summary>
    [Fact]
    public void AnalyzeRejectsCaretPastDocumentEnd()
    {
        KustoLanguageService languageService = new();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => languageService.Analyze(string.Empty, 1, CreateDatabaseSchema()));
    }

    /// <summary>
    /// Verifies that a pre-canceled request does not perform language analysis.
    /// </summary>
    [Fact]
    public void AnalyzeHonorsPreCanceledToken()
    {
        using CancellationTokenSource cancellationSource = new();
        cancellationSource.Cancel();
        KustoLanguageService languageService = new();

        Assert.Throws<OperationCanceledException>(
            () => languageService.Analyze(
                string.Empty,
                0,
                CreateDatabaseSchema(),
                cancellationSource.Token));
    }

    /// <summary>
    /// Verifies that the caret selects the independent blank-line-delimited query block.
    /// </summary>
    [Fact]
    public void GetQueryAtPositionSelectsSecondIndependentQuery()
    {
        const string Document = "StormEvents | take 1\n\nStormEvents | count";
        int caretPosition = Document.LastIndexOf("count", StringComparison.Ordinal);
        KustoLanguageService languageService = new();

        KustoQuerySelection? selection = languageService.GetQueryAtPosition(Document, caretPosition);

        Assert.NotNull(selection);
        Assert.Equal("StormEvents | count", selection.Text);
        Assert.Equal(Document.LastIndexOf("StormEvents", StringComparison.Ordinal), selection.Start);
        Assert.Equal(selection.Text.Length, selection.Length);
    }

    /// <summary>
    /// Verifies that semicolon-separated declarations and their final expression remain one query block.
    /// </summary>
    [Fact]
    public void GetQueryAtPositionKeepsSemicolonStatementsTogether()
    {
        const string Document = "let threshold = 10;\nStormEvents | where DamageProperty > threshold";
        KustoLanguageService languageService = new();

        KustoQuerySelection? selection = languageService.GetQueryAtPosition(Document, Document.Length);

        Assert.NotNull(selection);
        Assert.Equal(Document, selection.Text);
        Assert.Equal(0, selection.Start);
    }

    /// <summary>
    /// Verifies that a caret in extra separator whitespace selects the nearest preceding executable block.
    /// </summary>
    [Fact]
    public void GetQueryAtPositionUsesNearestBlockInSeparatorWhitespace()
    {
        const string Document = "StormEvents | take 1\n\n\nStormEvents | count";
        int caretPosition = Document.IndexOf("\n\n\n", StringComparison.Ordinal) + 2;
        KustoLanguageService languageService = new();

        KustoQuerySelection? selection = languageService.GetQueryAtPosition(Document, caretPosition);

        Assert.NotNull(selection);
        Assert.Equal("StormEvents | take 1", selection.Text);
    }

    /// <summary>
    /// Verifies that whitespace-only documents contain no executable query.
    /// </summary>
    [Fact]
    public void GetQueryAtPositionReturnsNullForWhitespaceDocument()
    {
        KustoLanguageService languageService = new();

        KustoQuerySelection? selection = languageService.GetQueryAtPosition(" \n\t", 2);

        Assert.Null(selection);
    }

    /// <summary>
    /// Verifies a terminal make-graph expression is exported into named node and edge tables.
    /// </summary>
    [Fact]
    public void GetGraphQueryPlanCreatesMakeGraphExport()
    {
        const string Document = """
            let edges = datatable(source:string, target:string)["a", "b"];
            edges | make-graph source --> target
            """;
        KustoLanguageService languageService = new();

        KustoGraphQueryPlan? plan = languageService.GetGraphQueryPlanAtPosition(Document, Document.Length);

        Assert.NotNull(plan);
        Assert.Equal(KustoGraphSourceKind.MakeGraph, plan.SourceKind);
        Assert.Equal("source", plan.SourceColumnName);
        Assert.Equal("target", plan.TargetColumnName);
        Assert.Equal(Document, plan.Selection.Text);
        Assert.Contains("let edges", plan.ExportQueryText, StringComparison.Ordinal);
        Assert.Contains("graph-to-table nodes as", plan.ExportQueryText, StringComparison.Ordinal);
        Assert.Contains($"edges as {plan.EdgeTableName}", plan.ExportQueryText, StringComparison.Ordinal);
        Assert.Contains($"with_target_id={plan.TargetHashColumnName}", plan.ExportQueryText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a transient graph assigned to a let variable is exported when returned by name.
    /// </summary>
    [Fact]
    public void GetGraphQueryPlanCreatesNamedMakeGraphExport()
    {
        const string Document = """
            let rawLogs = datatable (rawLog: string) [
                "31.56.96.51 - - [2019-01-22 03:54:16 +0330] \"GET /product/27 HTTP/1.1\" 200 5379 \"https://www.contoso.com/m/filter/b113\" \"some client\" \"-\"",
                "31.56.96.51 - - [2019-01-22 03:55:17 +0330] \"GET /product/42 HTTP/1.1\" 200 5667 \"https://www.contoso.com/m/filter/b113\" \"some client\" \"-\"",
                "54.36.149.41 - - [2019-01-22 03:56:14 +0330] \"GET /product/27 HTTP/1.1\" 200 30577 \"-\" \"some client\" \"-\""
            ];
            let parsedLogs = rawLogs
                | parse rawLog with ipAddress: string " - - [" timestamp: datetime "] \"" httpVerb: string " " resource: string " " *
                | project-away rawLog;
            let edges = parsedLogs;
            let nodes =
                union
                    (parsedLogs
                    | distinct ipAddress
                    | project nodeId = ipAddress, label = "IP address"),
                    (parsedLogs | distinct resource | project nodeId = resource, label = "resource");
            let graph = edges
                | make-graph ipAddress --> resource with nodes on nodeId;
            graph
            """;
        KustoLanguageService languageService = new();

        KustoGraphQueryPlan? plan = languageService.GetGraphQueryPlanAtPosition(Document, Document.Length);

        Assert.NotNull(plan);
        Assert.Equal(KustoGraphSourceKind.MakeGraph, plan.SourceKind);
        Assert.Equal("ipAddress", plan.SourceColumnName);
        Assert.Equal("resource", plan.TargetColumnName);
        Assert.Contains("let graph = edges", plan.ExportQueryText, StringComparison.Ordinal);
        Assert.Contains(
            "graph\n| graph-to-table nodes as",
            plan.ExportQueryText.ReplaceLineEndings("\n"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies named-node graphs retain the documented direct pipeline when exported.
    /// </summary>
    [Fact]
    public void GetGraphQueryPlanExportsNamedNodeGraphDirectly()
    {
        const string Document = """
            let GraphEdges = datatable(SourceNodeId:string, TargetNodeId:string, Relationship:string)
                ["github:alice", "key:ssh-rsa", "OwnsKey"];
            let GraphNodes = datatable(NodeId:string, Label:string, NodeType:string, Value:string)
                ["github:alice", "@alice", "GitHubUser", "alice", "key:ssh-rsa", "ssh-rsa...", "PublicKey", "ssh-rsa"];
            GraphEdges
            | make-graph SourceNodeId --> TargetNodeId with GraphNodes on NodeId
            """;
        KustoLanguageService languageService = new();

        KustoGraphQueryPlan? plan = languageService.GetGraphQueryPlanAtPosition(Document, Document.Length);

        Assert.NotNull(plan);
        Assert.DoesNotContain("let __oke_graph_export", plan.ExportQueryText, StringComparison.Ordinal);
        Assert.Contains(
            "| make-graph SourceNodeId --> TargetNodeId with GraphNodes on NodeId\n| graph-to-table nodes as",
            plan.ExportQueryText.ReplaceLineEndings("\n"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a direct persistent graph function receives the same deterministic export treatment.
    /// </summary>
    [Fact]
    public void GetGraphQueryPlanCreatesPersistentGraphExport()
    {
        const string Document = "graph(\"SecurityGraph\")";
        KustoLanguageService languageService = new();

        KustoGraphQueryPlan? plan = languageService.GetGraphQueryPlanAtPosition(Document, Document.Length);

        Assert.NotNull(plan);
        Assert.Equal(KustoGraphSourceKind.GraphFunction, plan.SourceKind);
        Assert.Null(plan.SourceColumnName);
        Assert.Null(plan.TargetColumnName);
        Assert.Contains(Document, plan.ExportQueryText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies graph-match projections remain ordinary tabular queries rather than being re-exported.
    /// </summary>
    [Fact]
    public void GetGraphQueryPlanRejectsTabularGraphAnalysis()
    {
        const string Document = """
            graph("SecurityGraph")
            | graph-match (source)-[edge]->(target)
                project source, edge, target
            """;
        KustoLanguageService languageService = new();

        KustoGraphQueryPlan? plan = languageService.GetGraphQueryPlanAtPosition(Document, Document.Length);

        Assert.Null(plan);
    }

    /// <summary>
    /// Verifies generated export identifiers cannot collide with identifiers already present in the query.
    /// </summary>
    [Fact]
    public void GetGraphQueryPlanAvoidsInternalNameCollisions()
    {
        const string Document = """
            let __oke_graph_export = 42;
            graph("SecurityGraph")
            """;
        KustoLanguageService languageService = new();

        KustoGraphQueryPlan? plan = languageService.GetGraphQueryPlanAtPosition(Document, Document.Length);

        Assert.NotNull(plan);
        Assert.StartsWith("__oke_graph_export_1", plan.NodeTableName, StringComparison.Ordinal);
        Assert.Contains("let __oke_graph_export = 42;", plan.ExportQueryText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies graph planning uses only the independent query block selected by the caret.
    /// </summary>
    [Fact]
    public void GetGraphQueryPlanUsesCaretSelectedBlock()
    {
        const string Document = "StormEvents | count\n\ngraph(\"SecurityGraph\")";
        KustoLanguageService languageService = new();

        KustoGraphQueryPlan? plan = languageService.GetGraphQueryPlanAtPosition(Document, Document.Length);

        Assert.NotNull(plan);
        Assert.Equal("graph(\"SecurityGraph\")", plan.Selection.Text);
        Assert.DoesNotContain("StormEvents", plan.ExportQueryText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that render instructions are recovered from the caret-selected query block.
    /// </summary>
    [Fact]
    public void GetVisualizationAtPositionFindsTerminalTimeChart()
    {
        const string Document = "StormEvents | count\n\nStormEvents\n| summarize Events=count() by bin(StartTime, 1h)\n| render timechart";
        KustoLanguageService languageService = new();

        KustoVisualization? visualization = languageService.GetVisualizationAtPosition(
            Document,
            Document.Length);

        Assert.NotNull(visualization);
        Assert.Equal(KustoVisualizationKind.TimeChart, visualization.Kind);
    }

    /// <summary>
    /// Verifies that diagnostics from later query blocks retain full-document offsets.
    /// </summary>
    [Fact]
    public void AnalyzeOffsetsDiagnosticsAcrossIndependentQueries()
    {
        const string Document = "StormEvents | project State\n\nStormEvents | project MissingColumn";
        int missingColumnStart = Document.IndexOf("MissingColumn", StringComparison.Ordinal);
        KustoLanguageService languageService = new();

        KustoLanguageAnalysis analysis = languageService.Analyze(
            Document,
            missingColumnStart,
            CreateDatabaseSchema());

        KustoDiagnostic diagnostic = Assert.Single(
            analysis.Diagnostics,
            item => item.Message.Contains("MissingColumn", StringComparison.Ordinal));
        Assert.Equal(missingColumnStart, diagnostic.Start);
        int secondTableStart = Document.LastIndexOf("StormEvents", StringComparison.Ordinal);
        int secondOperatorStart = Document.LastIndexOf("project", StringComparison.Ordinal);
        Assert.Contains(
            analysis.Classifications,
            classification => classification.Start == secondTableStart
                && classification.Length == "StormEvents".Length
                && classification.Kind == "Table");
        Assert.Contains(
            analysis.Classifications,
            classification => classification.Start == secondOperatorStart
                && classification.Length == "project".Length
                && classification.Kind == "QueryOperator");
    }

    /// <summary>
    /// Verifies that completion in a later query uses a document-relative replacement range.
    /// </summary>
    [Fact]
    public void AnalyzeOffsetsCompletionAcrossIndependentQueries()
    {
        const string Document = "StormEvents | take 1\n\nStormEvents\n| where ";
        KustoLanguageService languageService = new();

        KustoLanguageAnalysis analysis = languageService.Analyze(
            Document,
            Document.Length,
            CreateDatabaseSchema());

        Assert.Contains(
            analysis.Completions,
            completion => completion.DisplayText.Equals("State", StringComparison.Ordinal));
        Assert.Equal(Document.Length, analysis.CompletionEditStart);
        Assert.Equal(0, analysis.CompletionEditLength);
    }

    private static bool IsClassification(
        string query,
        KustoClassification classification,
        string expectedText,
        string expectedKind)
    {
        string classifiedText = query.Substring(classification.Start, classification.Length);
        bool isMatch = classifiedText.Equals(expectedText, StringComparison.Ordinal)
            && classification.Kind.Equals(expectedKind, StringComparison.Ordinal);

        return isMatch;
    }

    private static KustoDatabaseSchema CreateDatabaseSchema()
    {
        KustoColumnSchema[] columns =
        [
            new("StartTime", KustoScalarType.DateTime),
            new("State", KustoScalarType.Text),
            new("DamageProperty", KustoScalarType.WideInteger),
        ];
        KustoTableSchema stormEvents = new("StormEvents", columns);
        KustoDatabaseSchema databaseSchema = new(
            "help.kusto.windows.net",
            "Samples",
            [stormEvents]);

        return databaseSchema;
    }
}
