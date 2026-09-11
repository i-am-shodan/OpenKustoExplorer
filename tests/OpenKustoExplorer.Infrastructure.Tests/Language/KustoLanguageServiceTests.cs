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
    /// Verifies core query operators receive concise catalog help when SDK quick info is empty.
    /// </summary>
    [Fact]
    public void GetSyntaxHelpDescribesWhereOperator()
    {
        const string Query = "StormEvents | where State == \"TX\"";
        int position = Query.IndexOf("where", StringComparison.Ordinal) + 2;
        KustoLanguageService languageService = new();

        KustoSyntaxHelp? help = languageService.GetSyntaxHelp(
            Query,
            position,
            CreateDatabaseSchema());

        Assert.NotNull(help);
        Assert.Equal("where", help.Title);
        Assert.Equal("Query operator", help.Kind);
        Assert.Equal("T | where Predicate", help.Signature);
        Assert.Contains("Filters the input rows", help.Description, StringComparison.Ordinal);
        Assert.Equal(Query.IndexOf("where", StringComparison.Ordinal), help.Start);
        Assert.Equal("where".Length, help.Length);
    }

    /// <summary>
    /// Verifies catalog help links the project operator to its official Microsoft Learn topic.
    /// </summary>
    [Fact]
    public void GetSyntaxHelpIncludesProjectDocumentation()
    {
        const string Query = "StormEvents | project State";
        int position = Query.IndexOf("project", StringComparison.Ordinal) + 2;
        KustoLanguageService languageService = new();

        KustoSyntaxHelp? help = languageService.GetSyntaxHelp(
            Query,
            position,
            CreateDatabaseSchema());

        Assert.NotNull(help);
        Assert.Equal("project", help.Title);
        Assert.NotNull(help.DocumentationUri);
        Assert.Equal(Uri.UriSchemeHttps, help.DocumentationUri.Scheme);
        Assert.Equal("learn.microsoft.com", help.DocumentationUri.Host);
        Assert.Equal("/en-us/kusto/query/project-operator", help.DocumentationUri.AbsolutePath);
        Assert.Equal("?view=microsoft-fabric", help.DocumentationUri.Query);
    }

    /// <summary>
    /// Verifies built-in function help combines an SDK signature with a concise explanation.
    /// </summary>
    [Fact]
    public void GetSyntaxHelpDescribesBuiltInFunction()
    {
        const string Query = "print result = strlen(\"Kusto\")";
        int position = Query.IndexOf("strlen", StringComparison.Ordinal) + 2;
        KustoLanguageService languageService = new();

        KustoSyntaxHelp? help = languageService.GetSyntaxHelp(
            Query,
            position,
            CreateDatabaseSchema());

        Assert.NotNull(help);
        Assert.Equal("strlen", help.Title);
        Assert.Equal("Scalar function", help.Kind);
        Assert.Contains("strlen(string): long", help.Signature, StringComparison.Ordinal);
        Assert.Contains("number of characters", help.Description, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies SDK quick info supplies bound schema help when no catalog entry exists.
    /// </summary>
    [Fact]
    public void GetSyntaxHelpDescribesBoundColumn()
    {
        const string Query = "StormEvents | project State";
        int position = Query.IndexOf("State", StringComparison.Ordinal) + 2;
        KustoLanguageService languageService = new();

        KustoSyntaxHelp? help = languageService.GetSyntaxHelp(
            Query,
            position,
            CreateDatabaseSchema());

        Assert.NotNull(help);
        Assert.Equal("State", help.Title);
        Assert.Equal("Column", help.Kind);
        Assert.Contains("State", help.Signature, StringComparison.Ordinal);
        Assert.Contains("current query scope", help.Description, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a caret immediately after a token resolves help for that token.
    /// </summary>
    [Fact]
    public void GetSyntaxHelpAtTokenEndUsesPrecedingSyntax()
    {
        const string Query = "StormEvents | summarize count()";
        int position = Query.IndexOf("summarize", StringComparison.Ordinal) + "summarize".Length;
        KustoLanguageService languageService = new();

        KustoSyntaxHelp? help = languageService.GetSyntaxHelp(
            Query,
            position,
            CreateDatabaseSchema());

        Assert.NotNull(help);
        Assert.Equal("summarize", help.Title);
        Assert.Contains("Groups rows", help.Description, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a caret between a function name and its opening parenthesis resolves the function.
    /// </summary>
    [Fact]
    public void GetSyntaxHelpAtFunctionEndUsesFunctionName()
    {
        const string Query = "print result = strlen(\"Kusto\")";
        int position = Query.IndexOf("strlen", StringComparison.Ordinal) + "strlen".Length;
        KustoLanguageService languageService = new();

        KustoSyntaxHelp? help = languageService.GetSyntaxHelp(
            Query,
            position,
            CreateDatabaseSchema());

        Assert.NotNull(help);
        Assert.Equal("strlen", help.Title);
    }

    /// <summary>
    /// Verifies literal values do not produce generic syntax help with no actionable explanation.
    /// </summary>
    [Fact]
    public void GetSyntaxHelpIgnoresLiteralValue()
    {
        const string Query = "print result = \"Kusto\"";
        int position = Query.IndexOf("Kusto", StringComparison.Ordinal) + 2;
        KustoLanguageService languageService = new();

        KustoSyntaxHelp? help = languageService.GetSyntaxHelp(
            Query,
            position,
            CreateDatabaseSchema());

        Assert.Null(help);
    }

    /// <summary>
    /// Verifies symbolic comparisons receive the same contextual help as word operators.
    /// </summary>
    [Fact]
    public void GetSyntaxHelpDescribesSymbolicOperator()
    {
        const string Query = "StormEvents | where State == \"TX\"";
        int position = Query.IndexOf("==", StringComparison.Ordinal);
        KustoLanguageService languageService = new();

        KustoSyntaxHelp? help = languageService.GetSyntaxHelp(
            Query,
            position,
            CreateDatabaseSchema());

        Assert.NotNull(help);
        Assert.Equal("==", help.Title);
        Assert.Equal("Predicate operator", help.Kind);
        Assert.Contains("equal", help.Description, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies normal editor analysis warms contextual help for immediate F1 display.
    /// </summary>
    [Fact]
    public void AnalyzeIncludesSyntaxHelpAtCaret()
    {
        const string Query = "StormEvents | summarize count() by State";
        int position = Query.IndexOf("summarize", StringComparison.Ordinal) + 2;
        KustoLanguageService languageService = new();

        KustoLanguageAnalysis analysis = languageService.Analyze(
            Query,
            position,
            CreateDatabaseSchema());

        Assert.NotNull(analysis.SyntaxHelp);
        Assert.Equal("summarize", analysis.SyntaxHelp.Title);
    }

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
    /// Verifies that common pipeline continuations lead the completion list.
    /// </summary>
    [Fact]
    public void AnalyzeAfterPipeRanksCommonOperatorsFirst()
    {
        const string query = "StormEvents\n| ";
        KustoLanguageService languageService = new();

        KustoLanguageAnalysis analysis = languageService.Analyze(query, query.Length, CreateDatabaseSchema());

        Assert.Equal("where", analysis.Completions[0].DisplayText);
        Assert.Contains(
            analysis.Completions.Take(5),
            completion => completion.DisplayText.Equals("project", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that symbols in the current query outrank the generic function catalog.
    /// </summary>
    [Fact]
    public void AnalyzeInScalarExpressionRanksInScopeSymbolsBeforeBuiltInFunctions()
    {
        const string query = "let threshold = 10;\nStormEvents\n| where DamageProperty > ";
        KustoLanguageService languageService = new();

        KustoLanguageAnalysis analysis = languageService.Analyze(query, query.Length, CreateDatabaseSchema());
        int thresholdIndex = FindCompletionIndex(analysis, "threshold");
        int stateIndex = FindCompletionIndex(analysis, "State");
        int builtInFunctionIndex = analysis.Completions
            .Select((completion, index) => (completion, index))
            .First(pair => pair.completion.Kind.Equals("BuiltInFunction", StringComparison.Ordinal))
            .index;

        Assert.True(thresholdIndex >= 0);
        Assert.True(stateIndex >= 0);
        Assert.True(thresholdIndex < builtInFunctionIndex);
        Assert.True(stateIndex < builtInFunctionIndex);
    }

    /// <summary>
    /// Verifies that a tabular variable is preferred when starting a query after declarations.
    /// </summary>
    [Fact]
    public void AnalyzeAfterLetDeclarationRanksLocalTableFirst()
    {
        const string query = "let recentStorms = StormEvents | take 10;\n";
        KustoLanguageService languageService = new();

        KustoLanguageAnalysis analysis = languageService.Analyze(query, query.Length, CreateDatabaseSchema());

        Assert.Equal("recentStorms", analysis.Completions[0].DisplayText);
    }

    /// <summary>
    /// Verifies that an exact typed operator outranks longer prefix matches.
    /// </summary>
    [Fact]
    public void AnalyzeTypedOperatorPrefixRanksExactMatchFirst()
    {
        const string query = "StormEvents\n| project";
        KustoLanguageService languageService = new();

        KustoLanguageAnalysis analysis = languageService.Analyze(query, query.Length, CreateDatabaseSchema());

        Assert.Equal("project", analysis.Completions[0].DisplayText);
    }

    /// <summary>
    /// Verifies that aggregate functions lead while composing a summarize expression.
    /// </summary>
    [Fact]
    public void AnalyzeAfterSummarizeRanksAggregateFunctionsBeforeGenericFunctions()
    {
        const string query = "StormEvents\n| summarize ";
        KustoLanguageService languageService = new();

        KustoLanguageAnalysis analysis = languageService.Analyze(query, query.Length, CreateDatabaseSchema());
        int aggregateFunctionIndex = analysis.Completions
            .Select((completion, index) => (completion, index))
            .First(pair => pair.completion.Kind.Equals("AggregateFunction", StringComparison.Ordinal))
            .index;
        int builtInFunctionIndex = analysis.Completions
            .Select((completion, index) => (completion, index))
            .First(pair => pair.completion.Kind.Equals("BuiltInFunction", StringComparison.Ordinal))
            .index;

        Assert.True(aggregateFunctionIndex < builtInFunctionIndex);
    }

    /// <summary>
    /// Verifies that the previous pipeline stage influences the next operator selection.
    /// </summary>
    [Fact]
    public void AnalyzeAfterProjectUsesCorpusTransitionRanking()
    {
        const string query = "StormEvents\n| project State\n| ";
        KustoLanguageService languageService = new();

        KustoLanguageAnalysis analysis = languageService.Analyze(query, query.Length, CreateDatabaseSchema());

        Assert.Equal("extend", analysis.Completions[0].DisplayText);
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
    /// Verifies that a dot-prefixed management command is selected for execution.
    /// </summary>
    [Fact]
    public void GetQueryAtPositionSelectsManagementCommand()
    {
        const string Command = ".show tables";
        KustoLanguageService languageService = new();

        KustoQuerySelection? selection = languageService.GetQueryAtPosition(Command, Command.Length);

        Assert.NotNull(selection);
        Assert.Equal(Command, selection.Text);
        Assert.Equal(0, selection.Start);
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

    private static int FindCompletionIndex(KustoLanguageAnalysis analysis, string displayText)
    {
        return analysis.Completions
            .Select((completion, index) => (completion, index))
            .Where(pair => pair.completion.DisplayText.Equals(displayText, StringComparison.Ordinal))
            .Select(pair => pair.index)
            .DefaultIfEmpty(-1)
            .First();
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
