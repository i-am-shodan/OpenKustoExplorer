using System.Collections.Specialized;
using AvaloniaEdit.Document;
using OpenKustoExplorer.Application.Assistance;
using OpenKustoExplorer.Application.Automations;
using OpenKustoExplorer.Application.Connections;
using OpenKustoExplorer.Application.Dashboards;
using OpenKustoExplorer.Application.Documents;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Graphs;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Application.Sessions;
using OpenKustoExplorer.Desktop.Editor;
using OpenKustoExplorer.Domain.Schema;
using OpenKustoExplorer.Graph;
using OpenKustoExplorer.Graph.Query;
using OpenKustoExplorer.Infrastructure.Language;
using OpenKustoExplorer.Infrastructure.Sessions;
using OpenKustoExplorer.Presentation.Workbench;

namespace OpenKustoExplorer.Presentation.Tests.Workbench;

/// <summary>
/// Verifies the initial workbench state and language-analysis presentation behavior.
/// </summary>
public sealed class MainWindowViewModelTests
{
    /// <summary>
    /// Verifies that startup exposes a useful schema snapshot and runnable query.
    /// </summary>
    [Fact]
    public void ConstructorBuildsRunnableSchemaWorkspace()
    {
        StubKustoLanguageService languageService = new();

        MainWindowViewModel viewModel = CreateViewModel(languageService: languageService);

        SchemaTableViewModel table = Assert.Single(viewModel.Tables);
        Assert.Equal("StormEvents", table.Name);
        Assert.Equal(4, table.Columns.Count);
        Assert.Contains("StormEvents", viewModel.QueryText, StringComparison.Ordinal);
        Assert.True(viewModel.CanRunQuery);
        Assert.True(viewModel.RunQueryCommand.CanExecute(null));
    }

    /// <summary>
    /// Verifies that the New Query command adds and selects a blank document tab.
    /// </summary>
    [Fact]
    public void NewQueryCommandAddsDocumentTab()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        KustoDocumentViewModel originalDocument = Assert.Single(viewModel.Documents);

        viewModel.NewQueryCommand.Execute(null);

        Assert.Equal(2, viewModel.Documents.Count);
        Assert.Same(originalDocument, viewModel.Documents[0]);
        Assert.Same(viewModel.Documents[1], viewModel.SelectedDocument);
        Assert.Empty(viewModel.QueryText);
        Assert.Equal("Created Query 2", viewModel.StatusText);
        Assert.False(viewModel.CanRunQuery);
        Assert.False(viewModel.RunQueryCommand.CanExecute(null));
    }

    /// <summary>
    /// Verifies imported KQL files create collision-safe tabs that inherit the active target.
    /// </summary>
    [Fact]
    public void ImportKqlFilesCreatesTargetedTabsInPickerOrder()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        KustoDocumentViewModel original = Assert.Single(viewModel.Documents);

        IReadOnlyList<KustoDocumentViewModel> imported = viewModel.ImportKqlFiles(
        [
            new KustoQueryFileContent("Query 1.kql", "StormEvents | take 5"),
            new KustoQueryFileContent("query 1.kql", string.Empty),
        ]);

        Assert.Equal(2, imported.Count);
        Assert.Equal(["Query 1 2", "query 1 3"], imported.Select(document => document.Title));
        Assert.Equal("StormEvents | take 5", imported[0].Text);
        Assert.Empty(imported[1].Text);
        Assert.All(imported, document =>
        {
            Assert.Equal(original.ClusterUri, document.ClusterUri);
            Assert.Equal(original.DatabaseName, document.DatabaseName);
        });
        Assert.Same(imported[1], viewModel.SelectedDocument);
        Assert.Equal("Imported 2 KQL files", viewModel.StatusText);
    }

    /// <summary>
    /// Verifies Copilot receives active tab/schema context and proposals require explicit application.
    /// </summary>
    /// <returns>A task that completes after two deterministic Copilot turns.</returns>
    [Fact]
    public async Task CopilotProposalsReplaceOrCreateQueryTabsOnlyWhenApplied()
    {
        StubKustoCopilotService copilotService = new()
        {
            Reply = new KustoCopilotReply("Added a state summary.", "StormEvents | summarize count() by State"),
        };
        MainWindowViewModel viewModel = CreateViewModel(copilotService: copilotService);
        string originalQuery = viewModel.QueryText;
        viewModel.Copilot.Prompt = "Summarize events by state";

        await viewModel.Copilot.SendCommand.ExecuteAsync(null);

        Assert.NotNull(copilotService.Context);
        Assert.Equal(originalQuery, copilotService.Context.QueryText);
        Assert.Contains("StormEvents", copilotService.Context.SchemaText, StringComparison.Ordinal);
        Assert.Equal(originalQuery, viewModel.QueryText);
        Assert.True(viewModel.Copilot.HasProposal);
        Assert.Contains(
            "StormEvents | summarize count() by State",
            viewModel.Copilot.Messages[^1].Content,
            StringComparison.Ordinal);

        viewModel.Copilot.ApplyToCurrentTabCommand.Execute(null);

        Assert.Equal("StormEvents | summarize count() by State", viewModel.QueryText);
        Assert.False(viewModel.Copilot.HasProposal);

        int documentCount = viewModel.Documents.Count;
        copilotService.Reply = new KustoCopilotReply("Created a sample query.", "StormEvents | take 20");
        viewModel.Copilot.Prompt = "Create a sample query";
        await viewModel.Copilot.SendCommand.ExecuteAsync(null);
        viewModel.Copilot.CreateNewTabCommand.Execute(null);

        Assert.Equal(documentCount + 1, viewModel.Documents.Count);
        Assert.Equal("StormEvents | take 20", viewModel.QueryText);
    }

    /// <summary>
    /// Verifies a validated Copilot proposal can be added to the end of the current query tab.
    /// </summary>
    /// <returns>A task that completes after the proposal is appended.</returns>
    [Fact]
    public async Task CopilotProposalCanBeAppendedToCurrentQueryTab()
    {
        const string ExistingQuery = "StormEvents | take 5";
        const string ProposedQuery = "StormEvents | summarize Total=count() by State";
        MainWindowViewModel viewModel = CreateViewModel(
            copilotService: new StubKustoCopilotService
            {
                Reply = new KustoCopilotReply("Created a state summary.", ProposedQuery),
            });
        viewModel.QueryText = ExistingQuery;
        viewModel.CaretPosition = 4;
        viewModel.Copilot.Prompt = "Add a state summary";
        await viewModel.Copilot.SendCommand.ExecuteAsync(null);

        Assert.True(viewModel.Copilot.HasAppendProposal);
        Assert.True(viewModel.Copilot.AppendToCurrentTabCommand.CanExecute(null));

        viewModel.Copilot.AppendToCurrentTabCommand.Execute(null);

        Assert.Equal($"{ExistingQuery}\n\n{ProposedQuery}", viewModel.QueryText);
        Assert.Equal(viewModel.QueryText.Length, viewModel.CaretPosition);
        Assert.False(viewModel.Copilot.HasProposal);
        Assert.Equal("Added to end of current tab", viewModel.Copilot.StatusText);
    }

    /// <summary>
    /// Verifies applying a Copilot query creates one editor undo operation.
    /// </summary>
    /// <returns>A task that completes after the proposal is applied and undone.</returns>
    [Fact]
    public async Task CopilotAppliedQueryCanBeUndoneInEditor()
    {
        const string Proposal = "StormEvents | summarize Total=count() by State";
        MainWindowViewModel viewModel = CreateViewModel(
            copilotService: new StubKustoCopilotService
            {
                Reply = new KustoCopilotReply("Created a summary.", Proposal),
            });
        string originalQuery = viewModel.QueryText;
        viewModel.Copilot.Prompt = "Summarize by state";
        await viewModel.Copilot.SendCommand.ExecuteAsync(null);

        viewModel.Copilot.ApplyToCurrentTabCommand.Execute(null);
        TextDocument editorDocument = new(originalQuery);
        editorDocument.UndoStack.ClearAll();
        KustoEditorDocumentSynchronizer.ApplyUndoableText(editorDocument, viewModel.QueryText);

        Assert.Equal(Proposal, editorDocument.Text);
        Assert.True(editorDocument.UndoStack.CanUndo);

        editorDocument.UndoStack.Undo();

        Assert.Equal(originalQuery, editorDocument.Text);
    }

    /// <summary>
    /// Verifies Copilot receives the complete query tab by default and no tab text after opt-out.
    /// </summary>
    /// <returns>A task that completes after enabled and disabled sharing turns.</returns>
    [Fact]
    public async Task CopilotSharesCompleteTabContentOnlyWhenEnabled()
    {
        const string Document = "StormEvents | take 5\n\nStormEvents | summarize count() by State";
        StubKustoCopilotService copilotService = new();
        MainWindowViewModel viewModel = CreateViewModel(copilotService: copilotService);
        viewModel.QueryText = Document;
        viewModel.CaretPosition = Document.Length;

        Assert.True(viewModel.Copilot.ShareTabContent);
        viewModel.Copilot.Prompt = "Review the tab";
        await viewModel.Copilot.SendCommand.ExecuteAsync(null);

        Assert.Equal(Document, copilotService.Context!.QueryText);

        viewModel.Copilot.ShareTabContent = false;
        viewModel.Copilot.Prompt = "Answer without tab content";
        await viewModel.Copilot.SendCommand.ExecuteAsync(null);

        Assert.Empty(copilotService.Context!.QueryText);
    }

    /// <summary>
    /// Verifies invalid Copilot KQL is repaired privately with compiler diagnostics before display.
    /// </summary>
    /// <returns>A task that completes after the repaired proposal is visible.</returns>
    [Fact]
    public async Task CopilotRepairsInvalidKqlBeforePresentingIt()
    {
        const string InvalidQuery = "StormEvents | project MissingColumn";
        const string ValidQuery = "StormEvents | project State";
        StubKustoLanguageService languageService = new()
        {
            AnalysisFactory = text => text.Contains("MissingColumn", StringComparison.Ordinal)
                ? new KustoLanguageAnalysis(
                    [],
                    [],
                    [new KustoDiagnostic("KS142", "Error", "Unknown column MissingColumn", 22, 13)],
                    0,
                    0)
                : new KustoLanguageAnalysis([], [], [], 0, 0),
        };
        StubKustoCopilotService copilotService = new()
        {
            Replies = new Queue<KustoCopilotReply>(
            [
                new KustoCopilotReply("Here is the query.", InvalidQuery),
                new KustoCopilotReply("Corrected query.", ValidQuery),
            ]),
        };
        StubKustoQueryService queryService = new();
        MainWindowViewModel viewModel = CreateViewModel(
            languageService: languageService,
            queryService: queryService,
            copilotService: copilotService);
        viewModel.Copilot.Prompt = "Project the state";

        await viewModel.Copilot.SendCommand.ExecuteAsync(null);

        Assert.Equal(2, copilotService.Requests.Count);
        Assert.Equal("Project the state", copilotService.Requests[0]);
        Assert.Contains("Unknown column MissingColumn", copilotService.Requests[1], StringComparison.Ordinal);
        Assert.Contains("KS142", copilotService.Requests[1], StringComparison.Ordinal);
        Assert.DoesNotContain(
            viewModel.Copilot.Messages,
            message => message.Content.Contains(InvalidQuery, StringComparison.Ordinal));
        Assert.Equal(ValidQuery, viewModel.Copilot.ProposedQuery);
        Assert.Contains("Corrected query", viewModel.Copilot.Messages[^1].Content, StringComparison.Ordinal);
        Assert.Null(queryService.Request);
    }

    /// <summary>
    /// Verifies repeatedly invalid Copilot KQL is withheld after bounded repair attempts.
    /// </summary>
    /// <returns>A task that completes after validation exhausts its repair budget.</returns>
    [Fact]
    public async Task CopilotWithholdsKqlThatCannotBeRepaired()
    {
        const string InvalidQuery = "StormEvents | project MissingColumn";
        StubKustoLanguageService languageService = new()
        {
            AnalysisFactory = _ => new KustoLanguageAnalysis(
                [],
                [],
                [new KustoDiagnostic("KS142", "Error", "Unknown column MissingColumn", 22, 13)],
                0,
                0),
        };
        StubKustoCopilotService copilotService = new()
        {
            Replies = new Queue<KustoCopilotReply>(Enumerable.Range(0, 3)
                .Select(_ => new KustoCopilotReply("Invalid draft.", InvalidQuery))),
        };
        StubKustoQueryService queryService = new();
        MainWindowViewModel viewModel = CreateViewModel(
            languageService: languageService,
            queryService: queryService,
            copilotService: copilotService);
        viewModel.Copilot.Prompt = "Project the state";

        await viewModel.Copilot.SendCommand.ExecuteAsync(null);

        Assert.Equal(3, copilotService.Requests.Count);
        Assert.Empty(viewModel.Copilot.ProposedQuery);
        Assert.DoesNotContain(
            viewModel.Copilot.Messages,
            message => message.Content.Contains(InvalidQuery, StringComparison.Ordinal));
        Assert.Contains("could not produce valid KQL", viewModel.Copilot.Messages[^1].Content, StringComparison.OrdinalIgnoreCase);
        Assert.Null(queryService.Request);
    }

    /// <summary>
    /// Verifies applying a Copilot edit preserves queries outside the caret-selected block.
    /// </summary>
    /// <returns>A task that completes after the proposal is applied.</returns>
    [Fact]
    public async Task CopilotAppliesEditsToTheCaretSelectedQuery()
    {
        const string firstQuery = "StormEvents | take 5";
        const string secondQuery = "Events | take 10";
        int secondQueryStart = firstQuery.Length + (Environment.NewLine.Length * 2);
        StubKustoLanguageService languageService = new()
        {
            QuerySelection = new KustoQuerySelection(secondQuery, secondQueryStart, secondQuery.Length),
        };
        StubKustoCopilotService copilotService = new()
        {
            Reply = new KustoCopilotReply("Updated the selected query.", "Events | summarize count()"),
        };
        MainWindowViewModel viewModel = CreateViewModel(
            languageService: languageService,
            copilotService: copilotService);
        viewModel.QueryText = $"{firstQuery}{Environment.NewLine}{Environment.NewLine}{secondQuery}";
        viewModel.CaretPosition = secondQueryStart;
        viewModel.Copilot.Prompt = "Summarize the selected query";

        await viewModel.Copilot.SendCommand.ExecuteAsync(null);
        viewModel.Copilot.ApplyToCurrentTabCommand.Execute(null);

        Assert.Equal(
            $"{firstQuery}{Environment.NewLine}{Environment.NewLine}Events | summarize count()",
            viewModel.QueryText);
        Assert.Equal(viewModel.QueryText.Length, viewModel.CaretPosition);
    }

    /// <summary>
    /// Verifies complete Copilot conversations follow their originating query tab.
    /// </summary>
    /// <returns>A task that completes after both tab conversations are exercised.</returns>
    [Fact]
    public async Task CopilotConversationHistoryIsTabSpecific()
    {
        StubKustoCopilotService copilotService = new();
        MainWindowViewModel viewModel = CreateViewModel(copilotService: copilotService);
        KustoDocumentViewModel firstDocument = viewModel.SelectedDocument!;
        KustoCopilotViewModel firstConversation = viewModel.Copilot;
        firstConversation.Prompt = "Explain the first query";
        await firstConversation.SendCommand.ExecuteAsync(null);

        viewModel.NewQueryCommand.Execute(null);
        KustoDocumentViewModel secondDocument = viewModel.SelectedDocument!;
        KustoCopilotViewModel secondConversation = viewModel.Copilot;
        Assert.NotSame(firstConversation, secondConversation);
        Assert.Empty(secondConversation.Messages);
        secondConversation.Prompt = "Write the second query";
        await secondConversation.SendCommand.ExecuteAsync(null);

        viewModel.SelectedDocument = firstDocument;
        Assert.Same(firstConversation, viewModel.Copilot);
        Assert.Contains("Explain the first query", viewModel.Copilot.Messages[0].Content, StringComparison.Ordinal);
        Assert.DoesNotContain(
            viewModel.Copilot.Messages,
            message => message.Content.Contains("Write the second query", StringComparison.Ordinal));

        viewModel.SelectedDocument = secondDocument;
        Assert.Same(secondConversation, viewModel.Copilot);
        Assert.Contains("Write the second query", viewModel.Copilot.Messages[0].Content, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies an in-flight Copilot turn remains visible after its originating tab loses focus.
    /// </summary>
    /// <returns>A task that completes after the background turn finishes.</returns>
    [Fact]
    public async Task CopilotWorkingStateSpansScopedConversations()
    {
        TaskCompletionSource<KustoCopilotReply> pendingReply = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        StubKustoCopilotService copilotService = new()
        {
            PendingReply = pendingReply,
        };
        MainWindowViewModel viewModel = CreateViewModel(copilotService: copilotService);
        KustoCopilotViewModel originatingConversation = viewModel.Copilot;
        originatingConversation.Prompt = "Explain this query";

        Task sendTask = originatingConversation.SendCommand.ExecuteAsync(null);

        Assert.True(originatingConversation.IsBusy);
        Assert.True(viewModel.IsCopilotWorking);
        viewModel.NewQueryCommand.Execute(null);
        Assert.NotSame(originatingConversation, viewModel.Copilot);
        Assert.False(viewModel.Copilot.IsBusy);
        Assert.True(viewModel.IsCopilotWorking);

        pendingReply.SetResult(new KustoCopilotReply("Finished.", null));
        await sendTask;

        Assert.False(originatingConversation.IsBusy);
        Assert.False(viewModel.IsCopilotWorking);
    }

    /// <summary>
    /// Verifies model discovery participates in global assistant activity tracking.
    /// </summary>
    /// <returns>A task that completes after model discovery finishes.</returns>
    [Fact]
    public async Task CopilotModelDiscoverySetsGlobalWorkingState()
    {
        TaskCompletionSource<IReadOnlyList<KustoCopilotModel>> pendingModels = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        StubKustoCopilotService copilotService = new()
        {
            PendingModels = pendingModels,
        };
        MainWindowViewModel viewModel = CreateViewModel(copilotService: copilotService);
        viewModel.Copilot.Prompt = "Explain this query";
        Assert.True(viewModel.Copilot.CanSend);

        Task refreshTask = viewModel.Copilot.RefreshModelsCommand.ExecuteAsync(null);

        Assert.True(viewModel.Copilot.IsLoadingModels);
        Assert.True(viewModel.IsCopilotWorking);
        Assert.False(viewModel.Copilot.CanSend);
        Assert.True(viewModel.Copilot.CanCancel);
        pendingModels.SetResult(copilotService.AvailableModels);
        await refreshTask;

        Assert.False(viewModel.Copilot.IsLoadingModels);
        Assert.False(viewModel.IsCopilotWorking);
        Assert.True(viewModel.Copilot.CanSend);
        Assert.False(viewModel.Copilot.CanCancel);
    }

    /// <summary>
    /// Verifies unavailable AI providers do not impair the query workbench.
    /// </summary>
    /// <returns>A task that completes after provider failures and query execution are exercised.</returns>
    [Fact]
    public async Task UnavailableAIProviderDoesNotBlockQueryWorkbench()
    {
        StubKustoCopilotService copilotService = new()
        {
            SignInException = new InvalidOperationException("No Copilot subscription is available"),
            ModelsException = new InvalidOperationException("Provider is offline"),
            SendException = new InvalidOperationException("No provider account is available"),
        };
        StubKustoQueryService queryService = new()
        {
            Result = new KustoQueryResult(
                [
                    new KustoResultTable(
                        "PrimaryResult",
                        [new KustoResultColumn("Status", "string")],
                        [new KustoResultRow(["query-still-works"])]),
                ],
                TimeSpan.FromMilliseconds(5)),
        };
        MainWindowViewModel viewModel = CreateViewModel(
            queryService: queryService,
            copilotService: copilotService);

        await viewModel.Copilot.SignInCommand.ExecuteAsync(null);
        Assert.Contains("Sign in failed", viewModel.Copilot.StatusText, StringComparison.Ordinal);
        await viewModel.Copilot.RefreshModelsCommand.ExecuteAsync(null);
        Assert.Contains("Models unavailable", viewModel.Copilot.StatusText, StringComparison.Ordinal);
        viewModel.Copilot.Prompt = "Explain this query";
        await viewModel.Copilot.SendCommand.ExecuteAsync(null);

        Assert.Equal("Unavailable", viewModel.Copilot.StatusText);
        Assert.Contains(
            viewModel.Copilot.Messages,
            message => message.Content.Contains("No provider account", StringComparison.Ordinal));

        await viewModel.RunQueryCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasResultTable);
        Assert.Equal("query-still-works", Assert.Single(viewModel.ResultRows).Cells[0].Text);
    }

    /// <summary>
    /// Verifies switching providers updates capabilities and removes incompatible saved models.
    /// </summary>
    /// <returns>A task that completes after provider model discovery.</returns>
    [Fact]
    public async Task CopilotProviderSwitchIsolatesModelsAndCapabilities()
    {
        StubKustoCopilotService copilotService = new();
        MainWindowViewModel viewModel = CreateViewModel(copilotService: copilotService);
        viewModel.ApplyCopilotDefaults(new KustoCopilotDefaults(
            true,
            true,
            false,
            false,
            false,
            new KustoCopilotModel("github-default", "GitHub default")));
        await viewModel.Copilot.RefreshModelsCommand.ExecuteAsync(null);
        Assert.Contains(viewModel.Copilot.Models, model => model.Id == "github-default");

        copilotService.ProviderKind = KustoAIProviderKind.OpenAI;
        copilotService.ProviderDisplayName = "OpenAI";
        copilotService.AvailableModels =
        [
            new KustoCopilotModel("openai-model", "OpenAI model"),
        ];
        viewModel.RefreshCopilotProvider();
        await viewModel.Copilot.RefreshModelsCommand.ExecuteAsync(null);

        Assert.Equal("OpenAI for KQL", viewModel.CopilotTitle);
        Assert.False(viewModel.Copilot.CanSignIn);
        Assert.False(viewModel.Copilot.SupportsMcp);
        Assert.Contains(viewModel.Copilot.Models, model => model.Id == "openai-model");
        Assert.DoesNotContain(viewModel.Copilot.Models, model => model.Id == "github-default");
    }

    /// <summary>
    /// Verifies current results and Azure MCP are unavailable until explicit per-tab data consent.
    /// </summary>
    /// <returns>A task that completes after consent and model options are verified.</returns>
    [Fact]
    public async Task CopilotSharesResultDataAndAzureMcpOnlyAfterConsent()
    {
        KustoResultTable table = new(
            "Result 1",
            [new KustoResultColumn("SecretValue", "string")],
            [new KustoResultRow(["bounded-result-value"])]);
        StubKustoCopilotService copilotService = new();
        MainWindowViewModel viewModel = CreateViewModel(
            queryService: new StubKustoQueryService
            {
                Result = new KustoQueryResult([table], TimeSpan.FromMilliseconds(5)),
            },
            copilotService: copilotService);
        await viewModel.RunQueryCommand.ExecuteAsync(null);

        viewModel.Copilot.Prompt = "Use available context";
        await viewModel.Copilot.SendCommand.ExecuteAsync(null);
        Assert.Empty(copilotService.Context!.SharedDataText);
        Assert.False(copilotService.Options!.EnableAzureMcp);

        await viewModel.Copilot.RefreshModelsCommand.ExecuteAsync(null);
        viewModel.Copilot.SelectedModel = Assert.Single(
            viewModel.Copilot.Models,
            model => model.Id == "gpt-test");
        viewModel.Copilot.ShareResultData = true;
        viewModel.Copilot.EnableAzureMcp = true;
        viewModel.Copilot.EnableMicrosoftLearnMcp = true;
        viewModel.Copilot.Prompt = "Use the shared result";
        await viewModel.Copilot.SendCommand.ExecuteAsync(null);

        Assert.Contains("SecretValue", copilotService.Context!.SharedDataText, StringComparison.Ordinal);
        Assert.Contains("bounded-result-value", copilotService.Context.SharedDataText, StringComparison.Ordinal);
        Assert.Equal("gpt-test", copilotService.Options!.ModelId);
        Assert.True(copilotService.Options.EnableAzureMcp);
        Assert.True(copilotService.Options.EnableMicrosoftLearnMcp);
        Assert.False(copilotService.Options.ShareRecordedSessionData);

        viewModel.Copilot.ShareResultData = false;
        Assert.False(viewModel.Copilot.EnableAzureMcp);
    }

    /// <summary>
    /// Verifies Automation Copilot shares the selected retained run only after explicit consent.
    /// </summary>
    /// <returns>A task that completes after both consent states are sent.</returns>
    [Fact]
    public async Task AutomationCopilotSharesSelectedRunOnlyAfterConsent()
    {
        DateTimeOffset startedAtUtc = new(2026, 9, 4, 9, 30, 0, TimeSpan.Zero);
        KustoResultTable table = new(
            "Authentication anomalies",
            [new KustoResultColumn("FailedSignIns", "long")],
            [new KustoResultRow(["47"])]);
        KustoAutomationRun run = new(
            Guid.NewGuid(),
            startedAtUtc,
            startedAtUtc.AddMilliseconds(135),
            KustoAutomationRunStatus.Succeeded,
            null,
            new KustoQueryResult([table], TimeSpan.FromMilliseconds(135)));
        KustoAutomation automation = new(
            Guid.NewGuid(),
            "Authentication anomaly watch",
            new Uri("https://adx.contoso.com"),
            "Security",
            "IdentityEvents | summarize FailedSignIns=count()",
            TimeSpan.FromMinutes(15),
            startedAtUtc.AddDays(-1),
            startedAtUtc.AddMinutes(15),
            null,
            true,
            [run]);
        StubKustoCopilotService copilotService = new();
        MainWindowViewModel viewModel = CreateViewModel(
            automationStore: new StubKustoAutomationStore
            {
                Catalog = new KustoAutomationCatalog([automation]),
            },
            copilotService: copilotService);
        viewModel.ShowAutomationsCommand.Execute(null);

        viewModel.Copilot.Prompt = "Interpret the selected run";
        await viewModel.Copilot.SendCommand.ExecuteAsync(null);
        Assert.Empty(copilotService.Context!.SharedDataText);

        viewModel.Copilot.ShareResultData = true;
        viewModel.Copilot.Prompt = "Interpret the shared run";
        await viewModel.Copilot.SendCommand.ExecuteAsync(null);

        Assert.Equal(KustoCopilotScopeKind.Automation, copilotService.Context!.ScopeKind);
        Assert.Contains("FailedSignIns", copilotService.Context.SharedDataText, StringComparison.Ordinal);
        Assert.Contains("47", copilotService.Context.SharedDataText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies application Copilot defaults apply to current and subsequently created scopes.
    /// </summary>
    /// <returns>A task that completes after query and graph scopes are activated.</returns>
    [Fact]
    public async Task CopilotDefaultsApplyToCurrentAndFutureScopes()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        KustoCopilotDefaults defaults = new(
            shareTabContent: false,
            shareSchema: false,
            shareResultData: true,
            enableMicrosoftLearnMcp: true,
            enableAzureMcp: true);

        viewModel.ApplyCopilotDefaults(defaults);

        Assert.False(viewModel.Copilot.ShareTabContent);
        Assert.False(viewModel.Copilot.ShareSchema);
        Assert.True(viewModel.Copilot.ShareResultData);
        Assert.True(viewModel.Copilot.EnableMicrosoftLearnMcp);
        Assert.True(viewModel.Copilot.EnableAzureMcp);

        viewModel.NewQueryCommand.Execute(null);

        Assert.False(viewModel.Copilot.ShareTabContent);
        Assert.False(viewModel.Copilot.ShareSchema);
        Assert.True(viewModel.Copilot.ShareResultData);
        Assert.True(viewModel.Copilot.EnableMicrosoftLearnMcp);
        Assert.True(viewModel.Copilot.EnableAzureMcp);

        await viewModel.ShowGraphCommand.ExecuteAsync(null);

        Assert.False(viewModel.Copilot.ShareResultData);
        Assert.False(viewModel.Copilot.EnableAzureMcp);

        viewModel.ApplyCopilotDefaults(new KustoCopilotDefaults(true, false, false, true, true));

        Assert.False(viewModel.Copilot.ShareResultData);
        Assert.False(viewModel.Copilot.EnableAzureMcp);
    }

    /// <summary>
    /// Verifies successful Copilot model discovery confirms authenticated state.
    /// </summary>
    /// <returns>A task that completes after model discovery.</returns>
    [Fact]
    public async Task CopilotModelDiscoveryMarksTheScopeSignedIn()
    {
        MainWindowViewModel viewModel = CreateViewModel();

        Assert.False(viewModel.Copilot.IsSignedIn);

        await viewModel.Copilot.RefreshModelsCommand.ExecuteAsync(null);

        Assert.True(viewModel.Copilot.IsSignedIn);
    }

    /// <summary>
    /// Verifies the global model seeds new Copilot sessions without replacing a per-tab override.
    /// </summary>
    /// <returns>A task that completes after model defaults and overrides are applied.</returns>
    [Fact]
    public async Task CopilotDefaultModelSeedsSessionsAndPreservesTabOverride()
    {
        StubKustoCopilotService copilotService = new();
        MainWindowViewModel viewModel = CreateViewModel(copilotService: copilotService);
        await viewModel.Copilot.RefreshModelsCommand.ExecuteAsync(null);
        KustoCopilotModel testModel = Assert.Single(
            viewModel.Copilot.Models,
            model => model.Id == "gpt-test");
        KustoCopilotModel otherModel = Assert.Single(
            viewModel.Copilot.Models,
            model => model.Id == "gpt-other");

        viewModel.ApplyCopilotDefaults(new KustoCopilotDefaults(
            true,
            true,
            false,
            false,
            false,
            testModel));
        Assert.Equal("gpt-test", viewModel.Copilot.SelectedModel.Id);

        viewModel.Copilot.SelectedModel = Assert.Single(
            viewModel.Copilot.Models,
            model => model.Id == "auto");
        viewModel.ApplyCopilotDefaults(new KustoCopilotDefaults(
            true,
            true,
            false,
            false,
            false,
            otherModel));
        Assert.Equal("auto", viewModel.Copilot.SelectedModel.Id);

        viewModel.NewQueryCommand.Execute(null);

        Assert.Equal("gpt-other", viewModel.Copilot.SelectedModel.Id);
        viewModel.Copilot.Prompt = "Use the configured model";
        await viewModel.Copilot.SendCommand.ExecuteAsync(null);
        Assert.Equal("gpt-other", copilotService.Options?.ModelId);

        viewModel.ShowAutomationsCommand.Execute(null);
        Assert.Equal("gpt-other", viewModel.Copilot.SelectedModel.Id);

        await viewModel.ShowGraphCommand.ExecuteAsync(null);
        Assert.Equal("gpt-other", viewModel.Copilot.SelectedModel.Id);
    }

    /// <summary>
    /// Verifies the schema and cluster/database identity are shared by default and hidden after opt-out.
    /// </summary>
    /// <returns>A task that completes after default and opt-out schema sharing are verified.</returns>
    [Fact]
    public async Task CopilotSharesSchemaByDefaultAndHidesTargetOnOptOut()
    {
        StubKustoCopilotService copilotService = new();
        MainWindowViewModel viewModel = CreateViewModel(copilotService: copilotService);

        Assert.True(viewModel.Copilot.ShareSchema);

        viewModel.Copilot.Prompt = "Use the current schema";
        await viewModel.Copilot.SendCommand.ExecuteAsync(null);
        Assert.NotEqual(
            "(cluster and database hidden by user)",
            copilotService.Context!.TargetText);

        viewModel.Copilot.ShareSchema = false;
        viewModel.Copilot.Prompt = "Hide the schema and database";
        await viewModel.Copilot.SendCommand.ExecuteAsync(null);
        Assert.Equal(
            "(cluster and database hidden by user)",
            copilotService.Context!.TargetText);
        Assert.Empty(copilotService.Context.SchemaText);
    }

    /// <summary>
    /// Verifies that tab context actions rename, recolor, group, reorder, and autosave the document.
    /// </summary>
    [Fact]
    public void TabContextActionsPersistAndKeepGroupsTogether()
    {
        Guid groupedId = Guid.NewGuid();
        Guid ungroupedId = Guid.NewGuid();
        Guid editedId = Guid.NewGuid();
        StubKustoDocumentStore documentStore = new()
        {
            Workspace = new KustoDocumentWorkspace(
                [
                    new KustoDocument(groupedId, "Dashboard", string.Empty, 0, null, null, groupName: "Operations"),
                    new KustoDocument(ungroupedId, "Scratch", string.Empty, 0, null, null),
                    new KustoDocument(editedId, "Query", string.Empty, 0, null, null),
                ],
                editedId),
        };
        MainWindowViewModel viewModel = CreateViewModel(documentStore: documentStore);
        KustoDocumentViewModel editedDocument = viewModel.Documents[2];

        editedDocument.RenameCommand.Execute(null);
        viewModel.RenameTabTitle = "Incident timeline";
        viewModel.SaveRenameTabCommand.Execute(null);
        editedDocument.SetColorCommand.Execute("Teal");
        editedDocument.GroupCommand.Execute(null);
        viewModel.TabGroupName = "operations";
        viewModel.SaveGroupTabCommand.Execute(null);
        viewModel.Dispose();

        Assert.Equal("Incident timeline", editedDocument.Title);
        Assert.Equal(KustoDocumentTabColor.Teal, editedDocument.TabColor);
        Assert.Equal("Operations", editedDocument.GroupName);
        Assert.Equal([groupedId, editedId, ungroupedId], viewModel.Documents.Select(document => document.Id));
        Assert.Equal("Operations", Assert.Single(viewModel.ExistingTabGroups));
        Assert.NotNull(documentStore.SavedWorkspace);
        KustoDocument savedDocument = Assert.Single(
            documentStore.SavedWorkspace.Documents,
            document => document.Id == editedId);
        Assert.Equal("Incident timeline", savedDocument.Title);
        Assert.Equal(KustoDocumentTabColor.Teal, savedDocument.TabColor);
        Assert.Equal("Operations", savedDocument.GroupName);
    }

    /// <summary>
    /// Verifies dragging a grouped tab moves the complete group and preserves member order.
    /// </summary>
    [Fact]
    public void MoveDocumentBlockReordersWholeGroup()
    {
        Guid firstGroupId = Guid.NewGuid();
        Guid secondGroupId = Guid.NewGuid();
        Guid targetId = Guid.NewGuid();
        Guid trailingId = Guid.NewGuid();
        StubKustoDocumentStore documentStore = new()
        {
            Workspace = new KustoDocumentWorkspace(
                [
                    new KustoDocument(firstGroupId, "Dashboard", string.Empty, 0, null, null, groupName: "Operations"),
                    new KustoDocument(secondGroupId, "Timeline", string.Empty, 0, null, null, groupName: "Operations"),
                    new KustoDocument(targetId, "Scratch", string.Empty, 0, null, null),
                    new KustoDocument(trailingId, "Archive", string.Empty, 0, null, null),
                ],
                targetId),
        };
        MainWindowViewModel viewModel = CreateViewModel(documentStore: documentStore);

        viewModel.MoveDocumentBlock(viewModel.Documents[1], viewModel.Documents[2], placeAfter: true);
        viewModel.Dispose();

        Assert.Equal(
            [targetId, firstGroupId, secondGroupId, trailingId],
            viewModel.Documents.Select(document => document.Id));
        Assert.Equal(
            [targetId, firstGroupId, secondGroupId, trailingId],
            documentStore.SavedWorkspace!.Documents.Select(document => document.Id));
    }

    /// <summary>
    /// Verifies keyboard-style adjacent movement reorders a complete named tab group.
    /// </summary>
    [Fact]
    public void MoveDocumentBlockByOffsetReordersWholeGroup()
    {
        Guid firstId = Guid.NewGuid();
        Guid groupedFirstId = Guid.NewGuid();
        Guid groupedSecondId = Guid.NewGuid();
        StubKustoDocumentStore documentStore = new()
        {
            Workspace = new KustoDocumentWorkspace(
                [
                    new KustoDocument(firstId, "First", string.Empty, 0, null, null),
                    new KustoDocument(groupedFirstId, "Group A", string.Empty, 0, null, null, groupName: "Group"),
                    new KustoDocument(groupedSecondId, "Group B", string.Empty, 0, null, null, groupName: "Group"),
                ],
                groupedFirstId),
        };
        MainWindowViewModel viewModel = CreateViewModel(documentStore: documentStore);

        bool moved = viewModel.MoveDocumentBlock(viewModel.Documents[1], -1);
        bool movedPastEdge = viewModel.MoveDocumentBlock(viewModel.Documents[0], -1);
        viewModel.Dispose();

        Assert.True(moved);
        Assert.False(movedPastEdge);
        Assert.Equal(
            [groupedFirstId, groupedSecondId, firstId],
            viewModel.Documents.Select(document => document.Id));
    }

    /// <summary>
    /// Verifies an autosave failure stays visible until the latest snapshot is durably retried.
    /// </summary>
    /// <returns>A task that completes after the failed save is retried.</returns>
    [Fact]
    public async Task DocumentAutosaveFailureCanBeRetried()
    {
        StubKustoDocumentStore documentStore = new()
        {
            SaveException = new IOException("Disk is full."),
        };
        MainWindowViewModel viewModel = CreateViewModel(documentStore: documentStore);

        viewModel.QueryText = "print 'keep me'";
        await documentStore.SaveAttempted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(viewModel.HasDocumentSaveError);
        Assert.Contains("Disk is full", viewModel.DocumentSaveErrorText, StringComparison.Ordinal);

        documentStore.SaveException = null;
        viewModel.RetryDocumentSaveCommand.Execute(null);

        Assert.False(viewModel.HasDocumentSaveError);
        Assert.Equal("print 'keep me'", Assert.Single(documentStore.SavedWorkspace!.Documents).Text);
        viewModel.Dispose();
    }

    /// <summary>
    /// Verifies cross-tab search includes titles and KQL text and navigates to the selected match.
    /// </summary>
    [Fact]
    public void TabSearchFindsAndNavigatesAcrossDocuments()
    {
        Guid firstId = Guid.NewGuid();
        Guid secondId = Guid.NewGuid();
        StubKustoDocumentStore documentStore = new()
        {
            Workspace = new KustoDocumentWorkspace(
                [
                    new KustoDocument(firstId, "Storm dashboard", "StormEvents | take 5", 0, null, null),
                    new KustoDocument(secondId, "Scratch", "let target = 'storm';\nprint target", 0, null, null),
                ],
                firstId),
        };
        MainWindowViewModel viewModel = CreateViewModel(documentStore: documentStore);

        viewModel.TabSearchText = "storm";

        Assert.True(viewModel.IsTabSearchOpen);
        Assert.True(viewModel.HasTabSearchResults);
        Assert.Equal(3, viewModel.TabSearchResults.Count);
        KustoTabSearchResultViewModel selectedResult = Assert.Single(
            viewModel.TabSearchResults,
            result => result.DocumentTitle == "Scratch");
        viewModel.SelectTabSearchResultCommand.Execute(selectedResult);

        Assert.Equal(secondId, viewModel.SelectedDocument!.Id);
        Assert.Equal(14, viewModel.CaretPosition);
        Assert.Empty(viewModel.TabSearchText);
        Assert.False(viewModel.IsTabSearchOpen);
    }

    /// <summary>
    /// Verifies that schema filtering matches descendant columns and restores the source table list.
    /// </summary>
    [Fact]
    public void SchemaFilterMatchesColumnsAndRestoresTables()
    {
        MainWindowViewModel viewModel = CreateViewModel();

        viewModel.SchemaFilterText = "DamageProperty";

        Assert.Single(viewModel.VisibleTables);
        Assert.False(viewModel.HasNoSchemaMatches);

        viewModel.SchemaFilterText = "missing object";

        Assert.Empty(viewModel.VisibleTables);
        Assert.True(viewModel.HasNoSchemaMatches);

        viewModel.SchemaFilterText = string.Empty;

        Assert.Equal(viewModel.Tables, viewModel.VisibleTables);
        Assert.False(viewModel.HasNoSchemaMatches);
    }

    /// <summary>
    /// Verifies that successful execution projects the primary result table into visible columns and rows.
    /// </summary>
    /// <returns>A task that completes after command execution is verified.</returns>
    [Fact]
    public async Task RunQueryCommandPopulatesVisibleResults()
    {
        KustoResultTable table = new(
            "Result 1",
            [new KustoResultColumn("State", "string"), new KustoResultColumn("Events", "long")],
            [new KustoResultRow(["Texas", "42"])]);
        StubKustoQueryService queryService = new()
        {
            Result = new KustoQueryResult([table], TimeSpan.FromMilliseconds(125)),
        };
        MainWindowViewModel viewModel = CreateViewModel(queryService: queryService);

        await viewModel.RunQueryCommand.ExecuteAsync(null);

        Assert.NotNull(queryService.Request);
        Assert.Equal("Samples", queryService.Request.DatabaseName);
        Assert.Equal("help.kusto.windows.net", queryService.Request.ClusterUri.Host);
        Assert.Equal(2, viewModel.ResultColumns.Count);
        Assert.All(viewModel.ResultColumns, column => Assert.InRange(column.DisplayWidth, 84, 149));
        Assert.Equal(
            viewModel.ResultColumns.Sum(column => column.DisplayWidth),
            viewModel.ResultTableMinimumWidth);
        KustoResultRowViewModel row = Assert.Single(viewModel.ResultRows);
        Assert.Equal("Texas", row.Cells[0].Text);
        Assert.True(viewModel.HasResultTable);
        Assert.False(viewModel.HasQueryError);
        Assert.Equal("Connected", viewModel.ConnectionStatusText);
        Assert.Contains("125 ms", viewModel.ResultSummary, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the manual Run command records its selected query target and retained result.
    /// </summary>
    /// <returns>A task that completes after the recording is reloaded.</returns>
    [Fact]
    public async Task RunQueryCommandRecordsActiveSession()
    {
        string directoryPath = Path.Combine(
            Path.GetTempPath(),
            $"OpenKustoExplorer-MainRecording-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        string filePath = Path.Combine(directoryPath, "recorded-sessions.db");

        try
        {
            using SqliteKustoRecordedSessionStore store = new(filePath);
            KustoRecordedChainSearcher searcher = new(store);
            StubKustoCopilotService copilotService = new();
            MainWindowViewModel viewModel = CreateViewModel(
                queryService: new StubKustoQueryService
                {
                    Result = new KustoQueryResult(
                        [new KustoResultTable(
                            "PrimaryResult",
                            [new KustoResultColumn("Value", "string")],
                            [new KustoResultRow(["recorded-value"])])],
                        TimeSpan.FromMilliseconds(12)),
                },
                recordedSessionStore: store,
                predicateInterestExtractor: new KustoPredicateInterestExtractor(),
                recordedRelationExtractor: new KustoRecordedRelationExtractor(),
                recordedChainSearcher: searcher,
                recordedRelationPlanner: new KustoRecordedRelationPlanner(),
                recordedChainGenerator: new KustoRecordedChainQueryGenerator(),
                copilotService: copilotService);
            KustoCopilotViewModel queryConversation = viewModel.Copilot;
            await viewModel.Recording.OpenRecordingCommand.ExecuteAsync(null);
            viewModel.Recording.NewSessionName = "Workbench recording";
            await viewModel.Recording.StartRecordingCommand.ExecuteAsync(null);

            await viewModel.RunQueryCommand.ExecuteAsync(null);
            await viewModel.Recording.StopRecordingCommand.ExecuteAsync(null);

            Assert.True(viewModel.IsQueryWorkbenchView);

            await viewModel.ShowSessionsCommand.ExecuteAsync(null);

            Assert.True(viewModel.IsSessionsView);
            Assert.NotNull(viewModel.Recording.SelectedSession);
            Assert.NotNull(viewModel.Recording.SelectedExecution);
            Assert.NotNull(viewModel.Recording.SelectedTable);
            KustoCopilotViewModel sessionConversation = viewModel.Copilot;
            Assert.NotSame(queryConversation, sessionConversation);
            Assert.Equal("Test Copilot for Recorded Sessions", viewModel.CopilotTitle);
            Assert.True(viewModel.IsRecordedSessionCopilotScope);
            Assert.False(viewModel.GenerateRecordedChainCommand.CanExecute(null));
            KustoResultCellViewModel recordedCell = viewModel.Recording.SelectedTable.Rows[0].Cells[0];
            viewModel.Recording.SetResultContext(recordedCell);
            await viewModel.Recording.SetChainStartCommand.ExecuteAsync(null);
            Assert.False(viewModel.GenerateRecordedChainCommand.CanExecute(null));
            await viewModel.Recording.SetChainEndCommand.ExecuteAsync(null);
            Assert.True(viewModel.GenerateRecordedChainCommand.CanExecute(null));

            KustoRecordedSessionSummary summary = Assert.Single(await store.GetSessionsAsync());
            KustoRecordedSession session = Assert.IsType<KustoRecordedSession>(
                await store.GetSessionAsync(summary.Id));
            KustoRecordedExecution execution = Assert.Single(session.Executions);
            Assert.Equal(viewModel.QueryInfo.ExecutedQueryText, execution.QueryText);
            Assert.Equal("Samples", execution.DatabaseName);
            Assert.Equal("recorded-value", Assert.Single(Assert.Single(execution.Result!.Tables).Rows).Values[0]);

            sessionConversation.ShareResultData = true;
            sessionConversation.ShareTabContent = true;
            copilotService.Reply = new KustoCopilotReply(
                "Prepared a broader query.",
                "StormEvents | project State, EventType");
            sessionConversation.Prompt = "Rewrite this query with another field";
            await sessionConversation.SendCommand.ExecuteAsync(null);

            Assert.Equal(KustoCopilotScopeKind.RecordedSession, copilotService.Context?.ScopeKind);
            Assert.Equal(summary.Id, copilotService.Context?.RecordedSessionScope?.SessionId);
            Assert.Equal(execution.QueryText, copilotService.Context?.QueryText);
            Assert.DoesNotContain("recorded-value", copilotService.Context?.SchemaText, StringComparison.Ordinal);
            Assert.Equal(string.Empty, copilotService.Context?.SharedDataText);
            Assert.True(copilotService.Options?.ShareRecordedSessionData);
            Assert.True(sessionConversation.HasProposal);
            Assert.False(sessionConversation.HasApplyProposal);
            Assert.False(sessionConversation.HasAppendProposal);
            int documentCount = viewModel.Documents.Count;

            sessionConversation.CreateNewTabCommand.Execute(null);

            Assert.True(viewModel.IsQueryWorkbenchView);
            Assert.Equal(documentCount + 1, viewModel.Documents.Count);
            Assert.Equal("StormEvents | project State, EventType", viewModel.QueryText);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, true);
            }
        }
    }

    /// <summary>
    /// Verifies result columns always fit complete header text, sort state, and filter controls.
    /// </summary>
    /// <returns>A task that completes after header widths are projected.</returns>
    [Fact]
    public async Task ResultColumnsDoNotShrinkBelowCompleteHeaderWidth()
    {
        const string LongHeader = "ExceptionallyLongResultColumnNameThatExceedsTheContentWidthCap";
        KustoResultTable table = new(
            "Result 1",
            [
                new KustoResultColumn("Id", "long"),
                new KustoResultColumn(LongHeader, "string"),
            ],
            [new KustoResultRow(["1", "x"])]);
        StubKustoQueryService queryService = new()
        {
            Result = new KustoQueryResult([table], TimeSpan.FromMilliseconds(25)),
        };
        MainWindowViewModel viewModel = CreateViewModel(queryService: queryService);

        await viewModel.RunQueryCommand.ExecuteAsync(null);

        Assert.Equal(88, viewModel.ResultColumns[0].DisplayWidth);
        Assert.Equal((LongHeader.Length * 7) + 60, viewModel.ResultColumns[1].DisplayWidth);
        Assert.True(viewModel.ResultColumns[1].DisplayWidth > 360);
    }

    /// <summary>
    /// Verifies result search replaces the visible projection without publishing per-row collection changes.
    /// </summary>
    /// <returns>A task that completes after the visible rows are filtered.</returns>
    [Fact]
    public async Task ResultSearchPublishesSingleCollectionReset()
    {
        KustoResultTable table = new(
            "Result 1",
            [new KustoResultColumn("Name", "string"), new KustoResultColumn("City", "string")],
            [
                new KustoResultRow(["Alice", "Austin"]),
                new KustoResultRow(["Bob", "Boston"]),
                new KustoResultRow(["Albert", "Austin"]),
            ]);
        MainWindowViewModel viewModel = CreateViewModel(
            queryService: new StubKustoQueryService
            {
                Result = new KustoQueryResult([table], TimeSpan.FromMilliseconds(15)),
            });
        await viewModel.RunQueryCommand.ExecuteAsync(null);
        List<NotifyCollectionChangedEventArgs> changes = [];
        viewModel.ResultRows.CollectionChanged += (_, eventArguments) => changes.Add(eventArguments);

        viewModel.ResultSearchText = "Austin";

        NotifyCollectionChangedEventArgs change = Assert.Single(changes);
        Assert.Equal(NotifyCollectionChangedAction.Reset, change.Action);
        Assert.Equal(["Alice", "Albert"], viewModel.ResultRows.Select(row => row.Cells[0].Text));
    }

    /// <summary>
    /// Verifies result rows support typed sorting, row search, and simultaneous column filters.
    /// </summary>
    /// <returns>A task that completes after local result transformations are applied.</returns>
    [Fact]
    public async Task ResultViewSupportsSortingSearchAndMultipleColumnFilters()
    {
        KustoResultTable table = new(
            "Result 1",
            [
                new KustoResultColumn("Name", "string"),
                new KustoResultColumn("City", "string"),
                new KustoResultColumn("Score", "long"),
            ],
            [
                new KustoResultRow(["Alice", "Austin", "10"]),
                new KustoResultRow(["Bob", "Boston", "2"]),
                new KustoResultRow(["Alicia", "Albany", "20"]),
                new KustoResultRow(["Albert", "Austin", "15"]),
            ]);
        MainWindowViewModel viewModel = CreateViewModel(
            queryService: new StubKustoQueryService
            {
                Result = new KustoQueryResult([table], TimeSpan.FromMilliseconds(15)),
            });
        await viewModel.RunQueryCommand.ExecuteAsync(null);
        KustoResultColumnViewModel nameColumn = viewModel.ResultColumns[0];
        KustoResultColumnViewModel cityColumn = viewModel.ResultColumns[1];
        KustoResultColumnViewModel scoreColumn = viewModel.ResultColumns[2];

        viewModel.ToggleResultSort(scoreColumn);
        Assert.Equal(["2", "10", "15", "20"], viewModel.ResultRows.Select(row => row.Cells[2].Text));

        viewModel.ToggleResultSort(scoreColumn);
        Assert.Equal(["20", "15", "10", "2"], viewModel.ResultRows.Select(row => row.Cells[2].Text));

        viewModel.ToggleResultSort(scoreColumn);
        Assert.Equal(["10", "2", "20", "15"], viewModel.ResultRows.Select(row => row.Cells[2].Text));

        viewModel.ResultSearchText = "Austin";
        Assert.Equal(["Alice", "Albert"], viewModel.ResultRows.Select(row => row.Cells[0].Text));
        Assert.Contains("2 of 4 rows", viewModel.ResultViewSummary, StringComparison.Ordinal);
        viewModel.ClearResultFiltersCommand.Execute(null);

        nameColumn.SelectedFilterOption = nameColumn.FilterOptions.Single(option =>
            option.Operator == KustoResultFilterOperator.StartsWith);
        nameColumn.FilterText = "Al";
        cityColumn.SelectedFilterOption = cityColumn.FilterOptions.Single(option =>
            option.Operator == KustoResultFilterOperator.Equals);
        cityColumn.FilterText = "Austin";
        scoreColumn.SelectedFilterOption = scoreColumn.FilterOptions.Single(option =>
            option.Operator == KustoResultFilterOperator.GreaterThan);
        scoreColumn.FilterText = "10";

        KustoResultRowViewModel filteredRow = Assert.Single(viewModel.ResultRows);
        Assert.Equal("Albert", filteredRow.Cells[0].Text);
        Assert.Equal(3, viewModel.ActiveResultColumnFilterCount);

        viewModel.ClearResultColumnFilter(scoreColumn);
        Assert.Equal(["Alice", "Albert"], viewModel.ResultRows.Select(row => row.Cells[0].Text));

        viewModel.ClearResultFiltersCommand.Execute(null);
        Assert.Equal(4, viewModel.ResultRows.Count);
        Assert.Equal(0, viewModel.ActiveResultColumnFilterCount);
        Assert.False(viewModel.HasResultFilters);
    }

    /// <summary>
    /// Verifies extended result sorting composes typed keys in stable priority order.
    /// </summary>
    /// <returns>A task that completes after result projection.</returns>
    [Fact]
    public async Task ResultViewSupportsStableMultiColumnSort()
    {
        KustoResultTable table = new(
            "Result 1",
            [new KustoResultColumn("Region", "string"), new KustoResultColumn("Score", "long")],
            [
                new KustoResultRow(["B", "1"]),
                new KustoResultRow(["A", "3"]),
                new KustoResultRow(["B", "2"]),
                new KustoResultRow(["A", "1"]),
            ]);
        MainWindowViewModel viewModel = CreateViewModel(
            queryService: new StubKustoQueryService
            {
                Result = new KustoQueryResult([table], TimeSpan.Zero),
            });
        await viewModel.RunQueryCommand.ExecuteAsync(null);

        viewModel.ToggleResultSort(viewModel.ResultColumns[0]);
        viewModel.ToggleResultSort(viewModel.ResultColumns[1], extendSort: true);
        viewModel.ToggleResultSort(viewModel.ResultColumns[1], extendSort: true);

        Assert.Equal(
            ["A:3", "A:1", "B:2", "B:1"],
            viewModel.ResultRows.Select(row => $"{row.Cells[0].Text}:{row.Cells[1].Text}"));
        Assert.Equal(1, viewModel.ResultColumns[0].SortPriority);
        Assert.Equal(2, viewModel.ResultColumns[1].SortPriority);

        viewModel.ToggleResultSort(viewModel.ResultColumns[1]);
        viewModel.ToggleResultSort(viewModel.ResultColumns[1]);

        Assert.False(viewModel.ResultColumns[0].IsSortActive);
        Assert.Equal(1, viewModel.ResultColumns[1].SortPriority);
        Assert.Equal(["1", "1", "2", "3"], viewModel.ResultRows.Select(row => row.Cells[1].Text));
    }

    /// <summary>
    /// Verifies each query tab restores its own local result search, filters, and sort order.
    /// </summary>
    /// <returns>A task that completes after switching between independently transformed tabs.</returns>
    [Fact]
    public async Task ResultViewTransformsPersistPerQueryTab()
    {
        KustoResultTable table = new(
            "Result 1",
            [new KustoResultColumn("Name", "string"), new KustoResultColumn("Score", "long")],
            [
                new KustoResultRow(["Alice", "10"]),
                new KustoResultRow(["Bob", "2"]),
                new KustoResultRow(["Alicia", "20"]),
            ]);
        MainWindowViewModel viewModel = CreateViewModel(
            queryService: new StubKustoQueryService
            {
                Result = new KustoQueryResult([table], TimeSpan.FromMilliseconds(10)),
            });
        KustoDocumentViewModel firstDocument = viewModel.SelectedDocument!;
        await viewModel.RunQueryCommand.ExecuteAsync(null);
        KustoResultColumnViewModel firstNameColumn = viewModel.ResultColumns[0];
        firstNameColumn.SelectedFilterOption = firstNameColumn.FilterOptions.Single(option =>
            option.Operator == KustoResultFilterOperator.Contains);
        firstNameColumn.FilterText = "ali";
        viewModel.ToggleResultSort(viewModel.ResultColumns[1]);
        viewModel.ToggleResultSort(viewModel.ResultColumns[1]);
        Assert.Equal(["Alicia", "Alice"], viewModel.ResultRows.Select(row => row.Cells[0].Text));

        viewModel.NewQueryCommand.Execute(null);
        KustoDocumentViewModel secondDocument = viewModel.SelectedDocument!;
        Assert.NotSame(firstDocument, secondDocument);
        viewModel.QueryText = "StormEvents | take 10";
        Assert.True(viewModel.CanRunQuery);
        Assert.True(viewModel.RunQueryCommand.CanExecute(null));
        await viewModel.RunQueryCommand.ExecuteAsync(null);
        Assert.False(viewModel.HasQueryError);
        Assert.False(viewModel.HasResultFilters);
        Assert.Equal(["Alice", "Bob", "Alicia"], viewModel.ResultRows.Select(row => row.Cells[0].Text));
        KustoResultColumnViewModel secondNameColumn = viewModel.ResultColumns[0];
        secondNameColumn.SelectedFilterOption = secondNameColumn.FilterOptions.Single(option =>
            option.Operator == KustoResultFilterOperator.Equals);
        secondNameColumn.FilterText = "Bob";
        Assert.Equal("Bob", Assert.Single(viewModel.ResultRows).Cells[0].Text);

        viewModel.SelectedDocument = firstDocument;
        Assert.Equal("ali", viewModel.ResultColumns[0].FilterText);
        Assert.Equal(KustoResultSortDirection.Descending, viewModel.ResultColumns[1].SortDirection);
        Assert.Equal(["Alicia", "Alice"], viewModel.ResultRows.Select(row => row.Cells[0].Text));

        viewModel.SelectedDocument = secondDocument;
        Assert.Equal("Bob", Assert.Single(viewModel.ResultRows).Cells[0].Text);
        Assert.Equal(KustoResultSortDirection.None, viewModel.ResultColumns[1].SortDirection);
    }

    /// <summary>
    /// Verifies exports follow displayed transforms and retain the exact query that produced the result.
    /// </summary>
    /// <returns>A task that completes after query execution and export projection.</returns>
    [Fact]
    public async Task ResultExportsUseDisplayedOrderAndExecutedQuerySnapshot()
    {
        KustoResultTable table = new(
            "Result 1",
            [new KustoResultColumn("Name", "string"), new KustoResultColumn("Score", "long")],
            [
                new KustoResultRow(["Alicia", "20"]),
                new KustoResultRow(["Bob", "2"]),
                new KustoResultRow(["Alice", "10"]),
            ]);
        MainWindowViewModel viewModel = CreateViewModel(
            queryService: new StubKustoQueryService
            {
                Result = new KustoQueryResult([table], TimeSpan.FromMilliseconds(10)),
            });
        const string ExecutedQuery = "StormEvents | project Name=State, Score=DamageProperty";
        viewModel.QueryText = ExecutedQuery;

        await viewModel.RunQueryCommand.ExecuteAsync(null);
        KustoResultColumnViewModel nameColumn = viewModel.ResultColumns[0];
        nameColumn.SelectedFilterOption = nameColumn.FilterOptions.Single(option =>
            option.Operator == KustoResultFilterOperator.StartsWith);
        nameColumn.FilterText = "Ali";
        viewModel.ToggleResultSort(viewModel.ResultColumns[1]);
        viewModel.QueryText = "print Edited=true";

        string queryAndResults = viewModel.CreateQueryAndResultsClipboardText();
        string query = viewModel.CreateQueryClipboardText();
        string csv = System.Text.Encoding.UTF8.GetString(
            viewModel.CreateResultExport(KustoResultExportFormat.Csv).Content);
        string kql = viewModel.CreateKqlDatatable();

        Assert.StartsWith("Cluster: https://help.kusto.windows.net/", queryAndResults, StringComparison.Ordinal);
        Assert.Contains(
            $"{Environment.NewLine}Database: Samples{Environment.NewLine}{Environment.NewLine}"
                + ExecutedQuery
                + Environment.NewLine
                + Environment.NewLine,
            queryAndResults,
            StringComparison.Ordinal);
        Assert.Equal(ExecutedQuery, query);
        Assert.DoesNotContain("print Edited", queryAndResults, StringComparison.Ordinal);
        Assert.DoesNotContain("Bob", queryAndResults, StringComparison.Ordinal);
        Assert.True(queryAndResults.IndexOf("Alice", StringComparison.Ordinal)
            < queryAndResults.IndexOf("Alicia", StringComparison.Ordinal));
        Assert.DoesNotContain("Bob", csv, StringComparison.Ordinal);
        Assert.True(csv.IndexOf("Alice", StringComparison.Ordinal) < csv.IndexOf("Alicia", StringComparison.Ordinal));
        Assert.DoesNotContain("Bob", kql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that server render instructions automatically project results and open the visualization output.
    /// </summary>
    /// <returns>A task that completes after query execution and chart projection.</returns>
    [Fact]
    public async Task RunQueryCommandAutomaticallyRendersServerVisualization()
    {
        KustoResultTable table = new(
            "Result 1",
            [
                new KustoResultColumn("Timestamp", "System.DateTime"),
                new KustoResultColumn("Protocol", "System.String"),
                new KustoResultColumn("Events", "System.Int64"),
            ],
            [
                new KustoResultRow(["2026-07-22T10:00:00Z", "TCP", "12"]),
                new KustoResultRow(["2026-07-22T10:00:00Z", "UDP", "8"]),
                new KustoResultRow(["2026-07-22T11:00:00Z", "TCP", "18"]),
                new KustoResultRow(["2026-07-22T11:00:00Z", "UDP", "10"]),
            ]);
        MainWindowViewModel viewModel = CreateViewModel(
            queryService: new StubKustoQueryService
            {
                Result = new KustoQueryResult(
                    [table],
                    TimeSpan.FromMilliseconds(20),
                    new KustoVisualization(KustoVisualizationKind.TimeChart, title: "Traffic")),
            });

        await viewModel.RunQueryCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasVisualization);
        Assert.False(viewModel.ShowVisualizationChoices);
        Assert.NotNull(viewModel.Visualization);
        Assert.Equal(KustoVisualizationKind.TimeChart, viewModel.Visualization.Kind);
        Assert.Equal("Traffic", viewModel.Visualization.Title);
        Assert.Equal(["TCP", "UDP"], viewModel.Visualization.Series.Select(series => series.Name));
        Assert.Equal(1, viewModel.SelectedOutputTabIndex);
        Assert.Contains("2 series", viewModel.VisualizationMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies each query tab restores its own materialized rows, query details, and visualization.
    /// </summary>
    /// <returns>A task that completes after tab output isolation is verified.</returns>
    [Fact]
    public async Task QueryOutputsAndVisualizationsPersistAcrossTabChanges()
    {
        KustoResultTable table = new(
            "Result 1",
            [
                new KustoResultColumn("Timestamp", "System.DateTime"),
                new KustoResultColumn("Events", "System.Int64"),
            ],
            [new KustoResultRow(["2026-07-23T10:00:00Z", "12"])]);
        MainWindowViewModel viewModel = CreateViewModel(
            queryService: new StubKustoQueryService
            {
                Result = new KustoQueryResult(
                    [table],
                    TimeSpan.FromMilliseconds(25),
                    new KustoVisualization(KustoVisualizationKind.TimeChart)),
            });
        KustoDocumentViewModel firstDocument = viewModel.SelectedDocument!;

        await viewModel.RunQueryCommand.ExecuteAsync(null);
        Assert.True(viewModel.HasVisualization);

        viewModel.NewQueryCommand.Execute(null);
        KustoDocumentViewModel secondDocument = viewModel.SelectedDocument!;
        Assert.Empty(viewModel.ResultRows);
        Assert.False(viewModel.HasVisualization);

        viewModel.SelectedDocument = firstDocument;
        Assert.Single(viewModel.ResultRows);
        Assert.Equal("12", viewModel.ResultRows[0].Cells[1].Text);
        Assert.Equal(KustoVisualizationKind.TimeChart, viewModel.Visualization?.Kind);
        Assert.Equal("Completed", viewModel.QueryInfo.StatusText);

        viewModel.SelectedDocument = secondDocument;
        Assert.Empty(viewModel.ResultRows);
        Assert.False(viewModel.HasVisualization);
        Assert.Equal("No query has run", viewModel.QueryInfo.StatusText);
    }

    /// <summary>
    /// Verifies complete result values remain isolated with their query tabs and can be closed.
    /// </summary>
    /// <returns>A task that completes after full value output state is verified.</returns>
    [Fact]
    public async Task FullResultValuePersistsAcrossTabChangesAndClosesToResults()
    {
        string fullValue = "{\n  \"message\": \"A value too wide for the result cell\",\n  \"count\": 42\n}";
        KustoResultTable table = new(
            "Result 1",
            [new KustoResultColumn("Payload", "dynamic")],
            [new KustoResultRow([fullValue])]);
        MainWindowViewModel viewModel = CreateViewModel(
            queryService: new StubKustoQueryService
            {
                Result = new KustoQueryResult([table], TimeSpan.FromMilliseconds(10)),
            });
        KustoDocumentViewModel firstDocument = viewModel.SelectedDocument!;

        await viewModel.RunQueryCommand.ExecuteAsync(null);
        viewModel.OpenResultValue(Assert.Single(viewModel.ResultRows).Cells[0]);

        Assert.True(viewModel.HasInspectedResultValue);
        Assert.Equal(fullValue, viewModel.InspectedResultText);
        Assert.Equal("Payload", viewModel.InspectedResultTitle);
        Assert.Equal("Row 1 | dynamic", viewModel.InspectedResultMetadata);
        Assert.Equal(3, viewModel.SelectedOutputTabIndex);

        viewModel.NewQueryCommand.Execute(null);
        KustoDocumentViewModel secondDocument = viewModel.SelectedDocument!;
        Assert.False(viewModel.HasInspectedResultValue);

        viewModel.SelectedDocument = firstDocument;
        Assert.Equal(fullValue, viewModel.InspectedResultText);
        Assert.Equal(3, viewModel.SelectedOutputTabIndex);

        viewModel.SelectedDocument = secondDocument;
        Assert.False(viewModel.HasInspectedResultValue);
        viewModel.SelectedDocument = firstDocument;
        viewModel.CloseResultValueCommand.Execute(null);

        Assert.False(viewModel.HasInspectedResultValue);
        Assert.Equal(string.Empty, viewModel.InspectedResultText);
        Assert.Equal(0, viewModel.SelectedOutputTabIndex);
    }

    /// <summary>
    /// Verifies that a terminal render operator opens Visualization when the server omits metadata.
    /// </summary>
    /// <returns>A task that completes after fallback rendering.</returns>
    [Fact]
    public async Task RunQueryCommandUsesRenderSyntaxWhenMetadataIsMissing()
    {
        KustoResultTable table = new(
            "Result 1",
            [new KustoResultColumn("Timestamp", "datetime"), new KustoResultColumn("Events", "long")],
            [new KustoResultRow(["2026-07-22T10:00:00Z", "12"])]);
        MainWindowViewModel viewModel = CreateViewModel(
            queryService: new StubKustoQueryService
            {
                Result = new KustoQueryResult([table], TimeSpan.FromMilliseconds(10)),
            });
        viewModel.QueryText = "StormEvents\n| summarize Events=count() by bin(StartTime, 1h)\n| project Timestamp=StartTime, Events\n| render timechart";
        viewModel.CaretPosition = viewModel.QueryText.Length;

        await viewModel.RunQueryCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasVisualization);
        Assert.Equal(KustoVisualizationKind.TimeChart, viewModel.Visualization?.Kind);
        Assert.Equal(1, viewModel.SelectedOutputTabIndex);
    }

    /// <summary>
    /// Verifies result context filters are inserted before a terminal render and query details are populated.
    /// </summary>
    /// <returns>A task that completes after result interaction.</returns>
    [Fact]
    public async Task ResultContextAddsFilterBeforeRenderAndPopulatesQueryInfo()
    {
        KustoResultTable table = new(
            "Result 1",
            [new KustoResultColumn("State", "string"), new KustoResultColumn("Events", "long")],
            [new KustoResultRow(["Texas", "42"])]);
        MainWindowViewModel viewModel = CreateViewModel(
            queryService: new StubKustoQueryService
            {
                Result = new KustoQueryResult([table], TimeSpan.FromMilliseconds(25)),
            });
        viewModel.QueryText = "StormEvents\n| summarize Events=count() by State\n| render columnchart";
        viewModel.CaretPosition = viewModel.QueryText.Length;

        await viewModel.RunQueryCommand.ExecuteAsync(null);
        KustoResultCellViewModel cell = Assert.Single(viewModel.ResultRows).Cells[0];
        viewModel.SetResultContext(cell);
        viewModel.AddCellFilterCommand.Execute(null);

        int filterIndex = viewModel.QueryText.IndexOf("| where ['State'] == 'Texas'", StringComparison.Ordinal);
        int renderIndex = viewModel.QueryText.IndexOf("| render columnchart", StringComparison.Ordinal);
        Assert.True(filterIndex >= 0 && filterIndex < renderIndex);
        Assert.Equal("Completed", viewModel.QueryInfo.StatusText);
        Assert.Contains("1 tables", viewModel.QueryInfo.ResultShapeText, StringComparison.Ordinal);
        Assert.Contains("StormEvents", viewModel.QueryInfo.ExecutedQueryText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies multiple selected result values create one OR filter in the active query.
    /// </summary>
    /// <returns>A task that completes after result interaction.</returns>
    [Fact]
    public async Task ResultContextAddsSelectedValuesAsOrFilter()
    {
        KustoResultTable table = new(
            "Result 1",
            [new KustoResultColumn("State", "string")],
            [
                new KustoResultRow(["Texas"]),
                new KustoResultRow(["Ohio"]),
                new KustoResultRow(["Nevada"]),
            ]);
        MainWindowViewModel viewModel = CreateViewModel(
            queryService: new StubKustoQueryService
            {
                Result = new KustoQueryResult([table], TimeSpan.FromMilliseconds(10)),
            });
        viewModel.QueryText = "StormEvents | summarize by State";
        viewModel.CaretPosition = viewModel.QueryText.Length;
        await viewModel.RunQueryCommand.ExecuteAsync(null);
        viewModel.SetResultContext(viewModel.ResultRows[0].Cells[0]);

        viewModel.AddCellFilterFromResults([viewModel.ResultRows[0], viewModel.ResultRows[2]]);

        Assert.Contains(
            "| where (['State'] == 'Texas' or ['State'] == 'Nevada')",
            viewModel.QueryText,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies selected result values are copied from the context column in display order.
    /// </summary>
    /// <returns>A task that completes after result values are projected for the clipboard.</returns>
    [Fact]
    public async Task CreateSelectedResultValuesUsesContextColumnAndDisplayOrder()
    {
        KustoResultTable table = new(
            "Result 1",
            [new KustoResultColumn("Name", "string"), new KustoResultColumn("Score", "long")],
            [
                new KustoResultRow(["Alice", "10"]),
                new KustoResultRow(["Bob", "20"]),
                new KustoResultRow(["Charlie", "30"]),
            ]);
        MainWindowViewModel viewModel = CreateViewModel(
            queryService: new StubKustoQueryService
            {
                Result = new KustoQueryResult([table], TimeSpan.FromMilliseconds(10)),
            });
        await viewModel.RunQueryCommand.ExecuteAsync(null);
        viewModel.SetResultContext(viewModel.ResultRows[2].Cells[1]);

        string clipboardText = viewModel.CreateSelectedResultValues(
            [viewModel.ResultRows[2], viewModel.ResultRows[0]]);

        Assert.Equal($"30{Environment.NewLine}10", clipboardText);
    }

    /// <summary>
    /// Verifies complete datatable copies contain only the displayed result projection.
    /// </summary>
    /// <returns>A task that completes after the datatable is created.</returns>
    [Fact]
    public async Task CreateKqlDatatableUsesDisplayedResultRows()
    {
        KustoResultTable table = new(
            "Result 1",
            [new KustoResultColumn("Name", "string"), new KustoResultColumn("Score", "long")],
            [
                new KustoResultRow(["Alice", "10"]),
                new KustoResultRow(["Bob", "20"]),
                new KustoResultRow(["Charlie", "30"]),
            ]);
        MainWindowViewModel viewModel = CreateViewModel(
            queryService: new StubKustoQueryService
            {
                Result = new KustoQueryResult([table], TimeSpan.FromMilliseconds(10)),
            });
        await viewModel.RunQueryCommand.ExecuteAsync(null);
        viewModel.ResultSearchText = "Alice";

        string datatable = viewModel.CreateKqlDatatable();

        Assert.Single(viewModel.ResultRows);
        Assert.Contains("'Alice', 10", datatable, StringComparison.Ordinal);
        Assert.DoesNotContain("'Bob', 20", datatable, StringComparison.Ordinal);
        Assert.DoesNotContain("'Charlie', 30", datatable, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies conditional rules and alternating rows persist with the active query tab.
    /// </summary>
    /// <returns>A task that completes after rule creation and autosave flush.</returns>
    [Fact]
    public async Task ConditionalFormattingPersistsWithDocumentTab()
    {
        KustoResultTable table = new(
            "Result 1",
            [new KustoResultColumn("Events", "long")],
            [new KustoResultRow(["42"])]);
        StubKustoDocumentStore documentStore = new();
        MainWindowViewModel viewModel = CreateViewModel(
            queryService: new StubKustoQueryService
            {
                Result = new KustoQueryResult([table], TimeSpan.Zero),
            },
            documentStore: documentStore);

        await viewModel.RunQueryCommand.ExecuteAsync(null);
        viewModel.SelectedDocument!.UseAlternatingRows = true;
        viewModel.ConditionalRuleColumnName = "Events";
        viewModel.ConditionalRuleComparison = KustoConditionalFormatOperator.GreaterThanOrEqual;
        viewModel.ConditionalRuleComparisonValue = "40";
        viewModel.ConditionalRuleTarget = KustoConditionalFormatTarget.Cell;
        viewModel.ConditionalRuleColorHex = "#BBF7D0";
        viewModel.AddConditionalFormattingRuleCommand.Execute(null);
        viewModel.Dispose();

        Assert.NotNull(documentStore.SavedWorkspace);
        KustoDocument document = Assert.Single(documentStore.SavedWorkspace.Documents);
        Assert.True(document.UseAlternatingRows);
        KustoConditionalFormatRule rule = Assert.Single(document.ConditionalFormattingRules);
        Assert.Equal(KustoConditionalFormatOperator.GreaterThanOrEqual, rule.Comparison);
        Assert.Equal("#BBF7D0", rule.ColorHex);
    }

    /// <summary>
    /// Verifies that incompatible result columns produce actionable chart guidance.
    /// </summary>
    /// <returns>A task that completes after chart validation.</returns>
    [Fact]
    public async Task RenderVisualizationCommandExplainsMissingDateTimeColumn()
    {
        KustoResultTable table = new(
            "Result 1",
            [new KustoResultColumn("State", "System.String"), new KustoResultColumn("Events", "System.Int64")],
            [new KustoResultRow(["Texas", "42"])]);
        MainWindowViewModel viewModel = CreateViewModel(
            queryService: new StubKustoQueryService
            {
                Result = new KustoQueryResult([table], TimeSpan.Zero),
            });

        await viewModel.RunQueryCommand.ExecuteAsync(null);
        viewModel.RenderVisualizationCommand.Execute(nameof(KustoVisualizationKind.TimeChart));

        Assert.False(viewModel.HasVisualization);
        Assert.Equal(1, viewModel.SelectedOutputTabIndex);
        Assert.Contains("x-axis column", viewModel.VisualizationMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies Graph is a live-result visualization that opens the app-wide graph workspace.
    /// </summary>
    /// <returns>A task that completes after the result and Graph state load.</returns>
    [Fact]
    public async Task RenderGraphVisualizationOpensGraphWorkspace()
    {
        KustoResultTable table = new(
            "Result 1",
            [new KustoResultColumn("source", "string"), new KustoResultColumn("target", "string")],
            [new KustoResultRow(["alice", "server-1"])]);
        MainWindowViewModel viewModel = CreateViewModel(
            queryService: new StubKustoQueryService
            {
                Result = new KustoQueryResult([table], TimeSpan.Zero),
            });

        await viewModel.RunQueryCommand.ExecuteAsync(null);
        viewModel.RenderVisualizationCommand.Execute(nameof(KustoVisualizationKind.Graph));

        Assert.True(viewModel.IsGraphView);
        Assert.Contains(viewModel.VisualizationChoices, choice => choice.Kind == KustoVisualizationKind.Graph);
    }

    /// <summary>
    /// Verifies that execution failures remain visible without escaping the asynchronous command.
    /// </summary>
    /// <returns>A task that completes after failure presentation is verified.</returns>
    [Fact]
    public async Task RunQueryCommandPresentsExecutionFailure()
    {
        StubKustoQueryService queryService = new()
        {
            Failure = new InvalidOperationException("Authentication was canceled."),
        };
        MainWindowViewModel viewModel = CreateViewModel(queryService: queryService);

        await viewModel.RunQueryCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasQueryError);
        Assert.Equal("Authentication was canceled.", viewModel.QueryErrorText);
        Assert.Equal("Query failed", viewModel.ResultSummary);
        Assert.Equal("Query failed", viewModel.StatusText);
        Assert.False(viewModel.IsRunningQuery);
        Assert.False(viewModel.HasQueryErrorHighlight);
        Assert.Null(viewModel.QueryErrorHighlight);
    }

    /// <summary>
    /// Verifies a query failure can be sent to the active tab's validated Copilot repair workflow.
    /// </summary>
    /// <returns>A task that completes after Copilot proposes a repair without rerunning KQL.</returns>
    [Fact]
    public async Task QueryFailureCanBeFixedWithCopilot()
    {
        const string Query = "StormEvents | project MissingColumn";
        const string FixedQuery = "StormEvents | project State";
        StubKustoQueryService queryService = new()
        {
            Failure = new InvalidOperationException(
                "Semantic error: Failed to resolve column. [line:position=1:23]"),
        };
        StubKustoCopilotService copilotService = new()
        {
            Reply = new KustoCopilotReply("Corrected the query.", FixedQuery),
        };
        MainWindowViewModel viewModel = CreateViewModel(
            queryService: queryService,
            copilotService: copilotService);
        viewModel.QueryText = Query;
        viewModel.CaretPosition = Query.Length;
        Assert.False(viewModel.FixQueryWithCopilotCommand.CanExecute(null));

        await viewModel.RunQueryCommand.ExecuteAsync(null);

        Assert.True(viewModel.FixQueryWithCopilotCommand.CanExecute(null));
        Assert.Equal(1, queryService.ExecuteCount);

        viewModel.FixQueryWithCopilotCommand.Execute(null);
        await viewModel.Copilot.SendCommand.ExecutionTask!;

        Assert.True(viewModel.IsCopilotPanelOpen);
        Assert.Contains(Query, copilotService.Requests[0], StringComparison.Ordinal);
        Assert.Contains("Failed to resolve column", copilotService.Requests[0], StringComparison.Ordinal);
        Assert.Contains("Line 1, column 23", copilotService.Requests[0], StringComparison.Ordinal);
        Assert.Equal(FixedQuery, viewModel.Copilot.ProposedQuery);
        Assert.Equal(1, queryService.ExecuteCount);
    }

    /// <summary>
    /// Verifies service-relative line and column details map to the active query in the full document.
    /// </summary>
    /// <returns>A task that completes after the located execution failure is presented.</returns>
    [Fact]
    public async Task RunQueryCommandMapsExecutionErrorLocationToTheDocument()
    {
        const string FirstQuery = "StormEvents | take 1";
        const string SecondQuery = "StormEvents\n| where Missing == 1\n| count";
        string document = $"{FirstQuery}\n\n{SecondQuery}";
        int secondQueryStart = FirstQuery.Length + 2;
        StubKustoLanguageService languageService = new()
        {
            QuerySelection = new KustoQuerySelection(SecondQuery, secondQueryStart, SecondQuery.Length),
        };
        MainWindowViewModel viewModel = CreateViewModel(
            languageService: languageService,
            queryService: new StubKustoQueryService
            {
                Failure = new InvalidOperationException(
                    "Semantic error: Failed to resolve column. [line:position=2:9]"),
            });
        viewModel.QueryText = document;
        viewModel.CaretPosition = document.Length;

        await viewModel.RunQueryCommand.ExecuteAsync(null);

        KustoQueryErrorHighlight highlight = Assert.IsType<KustoQueryErrorHighlight>(
            viewModel.QueryErrorHighlight);
        Assert.True(viewModel.HasQueryErrorHighlight);
        Assert.Equal(document.IndexOf("| where", StringComparison.Ordinal), highlight.Start);
        Assert.Equal("| where Missing == 1".Length, highlight.Length);
        Assert.Equal(4, highlight.LineNumber);
        Assert.Equal(9, highlight.ColumnNumber);
        Assert.Equal("Line 4, column 9", viewModel.QueryErrorLocationText);

        viewModel.QueryText = $"{document} ";

        Assert.False(viewModel.HasQueryErrorHighlight);
        Assert.Null(viewModel.QueryErrorHighlight);
    }

    /// <summary>
    /// Verifies semantic failures without coordinates highlight the complete executed statement.
    /// </summary>
    /// <returns>A task that completes after statement fallback is presented.</returns>
    [Fact]
    public async Task RunQueryCommandHighlightsStatementWhenErrorHasNoLocation()
    {
        const string Query = "StormEvents\n| where Missing == 1";
        MainWindowViewModel viewModel = CreateViewModel(
            queryService: new StubKustoQueryService
            {
                Failure = new InvalidOperationException(
                    "Semantic error: Failed to resolve scalar expression named 'Missing'."),
            });
        viewModel.QueryText = Query;
        viewModel.CaretPosition = Query.Length;

        await viewModel.RunQueryCommand.ExecuteAsync(null);

        KustoQueryErrorHighlight highlight = Assert.IsType<KustoQueryErrorHighlight>(
            viewModel.QueryErrorHighlight);
        Assert.Equal(0, highlight.Start);
        Assert.Equal(Query.Length, highlight.Length);
        Assert.Equal(1, highlight.LineNumber);
        Assert.Null(highlight.ColumnNumber);
        Assert.Equal("Statement starting at line 1", viewModel.QueryErrorLocationText);
    }

    /// <summary>
    /// Verifies that Run sends only the independent query selected by the active tab's caret.
    /// </summary>
    /// <returns>A task that completes after the selected query request is verified.</returns>
    [Fact]
    public async Task RunQueryCommandExecutesCaretSelectedQueryOnly()
    {
        const string Document = "StormEvents | take 1\n\nStormEvents | count";
        int selectedStart = Document.LastIndexOf("StormEvents", StringComparison.Ordinal);
        StubKustoLanguageService languageService = new()
        {
            QuerySelection = new KustoQuerySelection(
                "StormEvents | count",
                selectedStart,
                "StormEvents | count".Length),
        };
        StubKustoQueryService queryService = new();
        MainWindowViewModel viewModel = CreateViewModel(
            languageService: languageService,
            queryService: queryService);
        viewModel.QueryText = Document;
        viewModel.CaretPosition = Document.Length - 2;

        await viewModel.RunQueryCommand.ExecuteAsync(null);

        Assert.NotNull(queryService.Request);
        Assert.Equal("StormEvents | count", queryService.Request.QueryText);
        Assert.Equal(Document, languageService.SelectionText);
        Assert.Equal(Document.Length - 2, languageService.SelectionCaretPosition);
    }

    /// <summary>
    /// Verifies a terminal graph query imports into the graph and updates the standard Results grid.
    /// </summary>
    /// <returns>A task that completes after graph ingestion.</returns>
    [Fact]
    public async Task RunQueryCommandImportsTerminalGraphAndUpdatesResults()
    {
        const string Query = "graph(\"SecurityGraph\")";
        StubKustoLanguageService languageService = new()
        {
            GraphQueryPlan = CreateGraphQueryPlan(Query),
        };
        StubKustoQueryService queryService = new();
        StubKustoGraphIngestionService graphIngestionService = new();
        MainWindowViewModel viewModel = CreateViewModel(
            languageService: languageService,
            queryService: queryService,
            graphIngestionService: graphIngestionService);
        viewModel.QueryText = Query;
        viewModel.CaretPosition = Query.Length;

        await viewModel.RunQueryCommand.ExecuteAsync(null);

        Assert.Null(queryService.Request);
        Assert.NotNull(graphIngestionService.Request);
        Assert.Equal(GraphImportMode.Add, graphIngestionService.Request.ImportMode);
        Assert.Equal(Query, graphIngestionService.Request.Query.QueryText);
        Assert.Equal(viewModel.SelectedDocument!.Id, graphIngestionService.Request.SourceId);
        Assert.True(viewModel.IsGraphView);
        Assert.False(viewModel.IsGraphImportChoiceOpen);
        Assert.Equal("Completed", viewModel.QueryInfo.StatusText);
        Assert.Contains("2 nodes", viewModel.QueryInfo.ResultShapeText, StringComparison.Ordinal);
        Assert.True(viewModel.HasResultTable);
        Assert.Equal(["_SId", "_TId", "relationship"], viewModel.ResultColumns.Select(column => column.Name));
        KustoResultRowViewModel resultRow = Assert.Single(viewModel.ResultRows);
        Assert.Equal(["1", "2", "AuthenticatedTo"], resultRow.Cells.Select(cell => cell.Text));
    }

    /// <summary>
    /// Verifies manual graph ingestion presents ambiguous values and returns the analyst's merge decision.
    /// </summary>
    /// <returns>A task that completes after identity resolution and graph ingestion.</returns>
    [Fact]
    public async Task RunQueryCommandPromptsForAmbiguousGraphIdentity()
    {
        const string Query = "graph(\"SecurityGraph\")";
        const string SourceNamespace = "help.kusto.windows.net/samples";
        GraphEntityKey user = new(GraphEntityKind.User, "User", "alice-id", SourceNamespace);
        GraphEntityKey person = new(GraphEntityKind.User, "Person", "alice-id", SourceNamespace);
        GraphEntityIdentityConflict conflict = new(
            GraphEntityIdentityMatchKind.CanonicalId,
            "alice-id",
            [
                new GraphEntityIdentityCandidate(user, "Alice", 1, true),
                new GraphEntityIdentityCandidate(person, "Alice", 1, false),
            ],
            user);
        StubKustoLanguageService languageService = new()
        {
            GraphQueryPlan = CreateGraphQueryPlan(Query),
        };
        StubKustoGraphIngestionService graphIngestionService = new()
        {
            IdentityConflict = conflict,
        };
        MainWindowViewModel viewModel = CreateViewModel(
            languageService: languageService,
            graphIngestionService: graphIngestionService);
        viewModel.QueryText = Query;
        viewModel.CaretPosition = Query.Length;
        TaskCompletionSource<bool> promptOpened = new(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.PropertyChanged += (_, eventArguments) =>
        {
            if (eventArguments.PropertyName == nameof(MainWindowViewModel.IsGraphIdentityResolutionOpen)
                && viewModel.IsGraphIdentityResolutionOpen)
            {
                promptOpened.TrySetResult(true);
            }
        };

        Task execution = viewModel.RunQueryCommand.ExecuteAsync(null);
        await promptOpened.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(viewModel.IsGraphIdentityResolutionOpen);
        Assert.Contains("canonical value", viewModel.GraphIdentityMatchText, StringComparison.Ordinal);
        Assert.Contains("Existing graph: Alice | type User", viewModel.GraphIdentityCandidatesText, StringComparison.Ordinal);
        Assert.Contains("Incoming result: Alice | type Person", viewModel.GraphIdentityCandidatesText, StringComparison.Ordinal);
        Assert.Contains("use type User", viewModel.GraphIdentityMergeText, StringComparison.Ordinal);
        viewModel.MergeGraphIdentityCommand.Execute(null);
        await execution;

        Assert.Equal(GraphIdentityResolutionDecision.Merge, graphIngestionService.IdentityResolutionDecision);
        Assert.False(viewModel.IsGraphIdentityResolutionOpen);
        Assert.True(viewModel.IsGraphView);
    }

    /// <summary>
    /// Verifies nonempty graph queries wait for and honor the analyst's Add or Replace decision.
    /// </summary>
    /// <param name="importMode">The selected import mode.</param>
    /// <returns>A task that completes after graph ingestion.</returns>
    [Theory]
    [InlineData(GraphImportMode.Add)]
    [InlineData(GraphImportMode.Replace)]
    public async Task RunQueryCommandWaitsForNonemptyGraphDecision(GraphImportMode importMode)
    {
        const string Query = "graph(\"SecurityGraph\")";
        StubKustoLanguageService languageService = new()
        {
            GraphQueryPlan = CreateGraphQueryPlan(Query),
        };
        StubKustoGraphIngestionService graphIngestionService = new();
        DateTimeOffset utcNow = DateTimeOffset.UtcNow;
        StubGraphStore graphStore = new()
        {
            State = new GraphStateSummary(Guid.NewGuid(), utcNow, utcNow, 12, 8, 2, 20),
        };
        MainWindowViewModel viewModel = CreateViewModel(
            languageService: languageService,
            graphIngestionService: graphIngestionService,
            graphStore: graphStore);
        viewModel.QueryText = Query;
        viewModel.CaretPosition = Query.Length;

        Task execution = viewModel.RunQueryCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsGraphImportChoiceOpen);
        Assert.Null(graphIngestionService.Request);
        Assert.Contains("12 nodes", viewModel.GraphImportChoiceSummary, StringComparison.Ordinal);

        if (importMode == GraphImportMode.Add)
        {
            viewModel.AddGraphImportCommand.Execute(null);
        }
        else
        {
            viewModel.ReplaceGraphImportCommand.Execute(null);
        }

        await execution;

        Assert.NotNull(graphIngestionService.Request);
        Assert.Equal(importMode, graphIngestionService.Request.ImportMode);
        Assert.False(viewModel.IsGraphImportChoiceOpen);
    }

    /// <summary>
    /// Verifies canceling the nonempty graph decision executes neither graph nor tabular query paths.
    /// </summary>
    /// <returns>A task that completes after the pending decision is canceled.</returns>
    [Fact]
    public async Task RunQueryCommandCancelsNonemptyGraphBeforeExecution()
    {
        const string Query = "graph(\"SecurityGraph\")";
        StubKustoLanguageService languageService = new()
        {
            GraphQueryPlan = CreateGraphQueryPlan(Query),
        };
        StubKustoQueryService queryService = new();
        StubKustoGraphIngestionService graphIngestionService = new();
        DateTimeOffset utcNow = DateTimeOffset.UtcNow;
        StubGraphStore graphStore = new()
        {
            State = new GraphStateSummary(Guid.NewGuid(), utcNow, utcNow, 1, 0, 1, 1),
        };
        MainWindowViewModel viewModel = CreateViewModel(
            languageService: languageService,
            queryService: queryService,
            graphIngestionService: graphIngestionService,
            graphStore: graphStore);
        viewModel.QueryText = Query;
        viewModel.CaretPosition = Query.Length;
        Task execution = viewModel.RunQueryCommand.ExecuteAsync(null);

        viewModel.CancelGraphImportCommand.Execute(null);
        await execution;

        Assert.Null(graphIngestionService.Request);
        Assert.Null(queryService.Request);
        Assert.False(viewModel.IsGraphImportChoiceOpen);
        Assert.Equal("Graph query canceled", viewModel.StatusText);
        Assert.Equal("Canceled", viewModel.QueryInfo.StatusText);
    }

    /// <summary>
    /// Verifies that a completed analysis replaces the visible diagnostics and summary.
    /// </summary>
    [Fact]
    public void ApplyAnalysisReplacesVisibleDiagnostics()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        KustoDiagnostic diagnostic = new("KS001", "Error", "Example problem", 4, 2);
        KustoLanguageAnalysis analysis = new([], [], [diagnostic], 0, 0);

        viewModel.ApplyAnalysis(analysis);

        KustoDiagnosticViewModel visibleDiagnostic = Assert.Single(viewModel.Diagnostics);
        Assert.Equal("KS001", visibleDiagnostic.Code);
        Assert.Equal("Example problem", visibleDiagnostic.Message);
        Assert.Equal("1 problem", viewModel.DiagnosticSummary);
        Assert.Equal("Query has problems", viewModel.StatusText);
    }

    /// <summary>
    /// Verifies that analysis delegates the active document and schema to the application service.
    /// </summary>
    [Fact]
    public void AnalyzeDelegatesToLanguageService()
    {
        StubKustoLanguageService languageService = new();
        MainWindowViewModel viewModel = CreateViewModel(languageService: languageService);

        KustoLanguageAnalysis analysis = viewModel.Analyze("StormEvents", 5);

        Assert.Same(languageService.Analysis, analysis);
        Assert.Equal("StormEvents", languageService.Text);
        Assert.Equal(5, languageService.CaretPosition);
        Assert.Equal("Samples", languageService.DatabaseSchema?.DatabaseName);
    }

    /// <summary>
    /// Verifies that persisted clusters restore the complete cluster, database, table, and column hierarchy.
    /// </summary>
    [Fact]
    public void ConstructorRestoresPersistedConnectionHierarchy()
    {
        KustoDatabaseSchema schema = CreateSchema("adx.contoso.com", "Telemetry", "Events");
        KustoConnectionCatalog catalog = new(
            [
                new KustoClusterConnection(
                    new Uri("https://adx.contoso.com"),
                    "Contoso ADX",
                    [new KustoDatabaseConnection("Telemetry", "Telemetry", schema)]),
            ]);
        StubKustoConnectionStore store = new() { Catalog = catalog };

        MainWindowViewModel viewModel = CreateViewModel(connectionStore: store);

        KustoClusterViewModel cluster = Assert.Single(viewModel.Clusters);
        Assert.Equal("Contoso ADX", cluster.DisplayName);
        KustoDatabaseViewModel database = Assert.Single(cluster.Databases);
        Assert.Equal("Telemetry", database.Name);
        SchemaTableViewModel table = Assert.Single(database.Tables);
        Assert.Equal("Events", table.Name);
        Assert.Equal("Timestamp", Assert.Single(table.Columns).Name);
        Assert.Equal("adx.contoso.com", viewModel.ClusterName);
        Assert.Equal("Telemetry", viewModel.DatabaseName);
    }

    /// <summary>
    /// Verifies that stored functions appear in a dedicated folder before database tables and participate in filtering.
    /// </summary>
    [Fact]
    public void ConstructorPlacesFunctionsFolderBeforeTables()
    {
        KustoDatabaseSchema schema = new(
            "adx.contoso.com",
            "Telemetry",
            [new KustoTableSchema("Events", [new KustoColumnSchema("Timestamp", KustoScalarType.DateTime)])],
            [new KustoFunctionSchema("RecentEvents", "(lookback: timespan)", "{ Events }", "Operations", "Recent telemetry")]);
        KustoConnectionCatalog catalog = new(
            [
                new KustoClusterConnection(
                    new Uri("https://adx.contoso.com"),
                    "Contoso ADX",
                    [new KustoDatabaseConnection("Telemetry", "Telemetry", schema)]),
            ]);
        MainWindowViewModel viewModel = CreateViewModel(
            connectionStore: new StubKustoConnectionStore { Catalog = catalog });
        KustoDatabaseViewModel database = Assert.Single(viewModel.Clusters[0].Databases);

        SchemaFunctionsFolderViewModel folder = Assert.IsType<SchemaFunctionsFolderViewModel>(database.VisibleSchemaItems[0]);
        Assert.IsType<SchemaTableViewModel>(database.VisibleSchemaItems[1]);
        Assert.Equal("RecentEvents(lookback: timespan)", Assert.Single(folder.VisibleFunctions).Signature);

        viewModel.SchemaFilterText = "telemetry";

        folder = Assert.IsType<SchemaFunctionsFolderViewModel>(Assert.Single(database.VisibleSchemaItems));
        Assert.Equal("RecentEvents", Assert.Single(folder.VisibleFunctions).Name);
    }

    /// <summary>
    /// Verifies that folder names group case-insensitively while unfiled clusters remain at Explorer root.
    /// </summary>
    [Fact]
    public void ConstructorGroupsClustersIntoSharedFoldersAndRoot()
    {
        KustoConnectionCatalog catalog = new(
            [
                CreateConnection("https://one.contoso.com", "One", "Production"),
                CreateConnection("https://two.contoso.com", "Two", "production"),
                CreateConnection("https://three.contoso.com", "Three", null),
            ]);

        MainWindowViewModel viewModel = CreateViewModel(
            connectionStore: new StubKustoConnectionStore { Catalog = catalog });

        KustoFolderViewModel folder = Assert.Single(viewModel.Folders);
        Assert.Equal("Production", folder.Name);
        Assert.Equal(2, folder.Clusters.Count);
        Assert.Same(folder, viewModel.VisibleExplorerItems[0]);
        KustoClusterViewModel rootCluster = Assert.IsType<KustoClusterViewModel>(viewModel.VisibleExplorerItems[1]);
        Assert.Equal("Three", rootCluster.DisplayName);

        viewModel.SchemaFilterText = "Production";

        Assert.Same(folder, Assert.Single(viewModel.VisibleExplorerItems));
        Assert.Equal(2, folder.VisibleClusters.Count);
    }

    /// <summary>
    /// Verifies that moving the final cluster out of a folder removes the folder and persists root placement.
    /// </summary>
    [Fact]
    public void OrganizeClusterMovesClusterToRootAndPersistsCatalog()
    {
        StubKustoConnectionStore store = new()
        {
            Catalog = new KustoConnectionCatalog(
                [CreateConnection("https://adx.contoso.com", "Contoso ADX", "Production")]),
        };
        MainWindowViewModel viewModel = CreateViewModel(connectionStore: store);
        KustoClusterViewModel cluster = Assert.Single(viewModel.Clusters);

        cluster.OrganizeCommand.Execute(null);
        viewModel.OrganizeFolderName = "  ";
        viewModel.SaveClusterFolderCommand.Execute(null);

        Assert.Empty(viewModel.Folders);
        Assert.Null(cluster.FolderName);
        Assert.Same(cluster, Assert.Single(viewModel.VisibleExplorerItems));
        Assert.False(viewModel.IsOrganizeClusterOpen);
        Assert.NotNull(store.SavedCatalog);
        Assert.Null(Assert.Single(store.SavedCatalog.Clusters).FolderName);
    }

    /// <summary>
    /// Verifies editing a cluster URL migrates live references while retaining historical run provenance.
    /// </summary>
    /// <returns>A task that completes after document autosave.</returns>
    [Fact]
    public async Task EditingClusterUrlMigratesPersistedReferences()
    {
        Uri oldClusterUri = new("https://old-adx.contoso.com");
        Uri newClusterUri = new("https://new-adx.contoso.com");
        DateTimeOffset timestamp = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
        KustoResultTable table = new(
            "PrimaryResult",
            [new KustoResultColumn("Count", "long")],
            [new KustoResultRow(["1"])]);
        KustoQueryResult cachedResult = new([table], TimeSpan.FromMilliseconds(5));
        KustoConnectionCatalog connectionCatalog = new(
        [
            new KustoClusterConnection(
                oldClusterUri,
                "Old ADX",
                [
                    new KustoDatabaseConnection(
                        "Telemetry",
                        "Telemetry",
                        CreateSchema(oldClusterUri.Host, "Telemetry", "Events")),
                ],
                "Production"),
        ]);
        StubKustoConnectionStore connectionStore = new() { Catalog = connectionCatalog };
        StubKustoDocumentStore documentStore = new()
        {
            Workspace = new KustoDocumentWorkspace(
                [new KustoDocument(Guid.NewGuid(), "Investigation", "Events | take 1", 0, oldClusterUri, "Telemetry")],
                null),
        };
        KustoDashboardWidget widget = new(
            Guid.NewGuid(),
            "Event count",
            oldClusterUri,
            "Telemetry",
            "Events | count",
            TimeSpan.FromMinutes(5),
            KustoDashboardWidgetDisplayMode.Table,
            KustoVisualizationKind.Table,
            new KustoDashboardWidgetLayout(0, 0, 16, 10),
            "#FFFFFF",
            "#1F2933",
            "#167D8D",
            cachedResult,
            timestamp);
        StubKustoDashboardStore dashboardStore = new()
        {
            Catalog = new KustoDashboardCatalog(
                [new KustoDashboard(Guid.NewGuid(), "Operations", "#FFFFFF", [widget])]),
        };
        KustoAutomationRun historicalRun = new(
            Guid.NewGuid(),
            timestamp,
            timestamp.AddSeconds(1),
            KustoAutomationRunStatus.Succeeded,
            null,
            cachedResult,
            oldClusterUri,
            "Telemetry");
        StubKustoAutomationStore automationStore = new()
        {
            Catalog = new KustoAutomationCatalog(
            [
                new KustoAutomation(
                    Guid.NewGuid(),
                    "Event monitor",
                    oldClusterUri,
                    "Telemetry",
                    "Events | count",
                    TimeSpan.FromMinutes(5),
                    timestamp,
                    timestamp.AddMinutes(5),
                    null,
                    true,
                    [historicalRun]),
            ]),
        };
        MainWindowViewModel viewModel = CreateViewModel(
            connectionStore: connectionStore,
            documentStore: documentStore,
            dashboardStore: dashboardStore,
            automationStore: automationStore);
        KustoClusterViewModel cluster = Assert.Single(viewModel.Clusters);

        cluster.EditCommand.Execute(null);
        Assert.Equal(oldClusterUri.AbsoluteUri, viewModel.OrganizeClusterAddress);
        Assert.Equal("1 query tabs, 1 widgets, 1 automations", viewModel.OrganizeClusterReferenceSummary);
        viewModel.OrganizeClusterAddress = newClusterUri.AbsoluteUri;
        viewModel.OrganizeClusterDisplayName = "New ADX";
        viewModel.SaveClusterFolderCommand.Execute(null);
        await documentStore.SaveAttempted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        KustoClusterViewModel migratedCluster = Assert.Single(viewModel.Clusters);
        Assert.Equal(newClusterUri, migratedCluster.ClusterUri);
        Assert.Equal("New ADX", migratedCluster.DisplayName);
        Assert.Equal("Production", migratedCluster.FolderName);
        Assert.Equal(newClusterUri, Assert.Single(viewModel.Documents).ClusterUri);
        Assert.Equal(newClusterUri, Assert.Single(connectionStore.SavedCatalog!.Clusters).ClusterUri);
        KustoDashboardWidget savedWidget = Assert.Single(Assert.Single(
            dashboardStore.SavedCatalog!.Dashboards).Widgets);
        Assert.Equal(newClusterUri, savedWidget.ClusterUri);
        Assert.Null(savedWidget.CachedResult);
        KustoAutomation savedAutomation = Assert.Single(automationStore.SavedCatalog!.Automations);
        Assert.Equal(newClusterUri, savedAutomation.ClusterUri);
        Assert.Equal(oldClusterUri, Assert.Single(savedAutomation.Runs).ClusterUri);
        Assert.False(viewModel.IsOrganizeClusterOpen);
    }

    /// <summary>
    /// Verifies that Add Cluster normalizes a host, discovers databases, loads the selected schema, and persists it.
    /// </summary>
    /// <returns>A task that completes after cluster discovery and schema loading are verified.</returns>
    [Fact]
    public async Task AddClusterCommandDiscoversAndPersistsHierarchy()
    {
        StubKustoCatalogService catalogService = new()
        {
            Databases = [new KustoDatabaseInfo("Telemetry", "Telemetry")],
            Schema = CreateSchema("adx.contoso.com", "Telemetry", "Events"),
        };
        StubKustoConnectionStore store = new();
        MainWindowViewModel viewModel = CreateViewModel(catalogService: catalogService, connectionStore: store);
        viewModel.OpenAddClusterCommand.Execute(null);
        viewModel.NewClusterAddress = "adx.contoso.com/path/to/database";
        viewModel.NewClusterDisplayName = "Contoso ADX";
        viewModel.NewClusterFolderName = "Production";

        await viewModel.AddClusterCommand.ExecuteAsync(null);
        Task? selectionTask = viewModel.SelectDatabaseCommand.ExecutionTask;
        if (selectionTask is not null)
        {
            await selectionTask;
        }

        KustoClusterViewModel cluster = Assert.Single(
            viewModel.Clusters,
            item => item.DisplayName == "Contoso ADX");
        Assert.Equal("https://adx.contoso.com/", cluster.ClusterUri.AbsoluteUri);
        Assert.Equal("Production", cluster.FolderName);
        KustoDatabaseViewModel database = Assert.Single(cluster.Databases);
        Assert.True(database.IsSchemaLoaded);
        Assert.Equal("Telemetry", viewModel.DatabaseName);
        Assert.NotNull(store.SavedCatalog);
        KustoClusterConnection savedCluster = Assert.Single(
            store.SavedCatalog.Clusters,
            item => item.DisplayName == "Contoso ADX");
        Assert.Equal("Production", savedCluster.FolderName);
        Assert.NotNull(Assert.Single(savedCluster.Databases).Schema);
    }

    /// <summary>
    /// Verifies legacy import merges new authorities without replacing existing connections.
    /// </summary>
    /// <returns>A task that completes after the import merge.</returns>
    [Fact]
    public async Task ImportKustoExplorerDataCommandMergesAndPersistsConnections()
    {
        Guid firstTabId = Guid.NewGuid();
        Guid secondTabId = Guid.NewGuid();
        StubKustoConnectionStore store = new()
        {
            Catalog = new KustoConnectionCatalog(
                [CreateConnection("https://adx.contoso.com", "Existing", null)]),
        };
        StubKustoExplorerImportService importService = new()
        {
            Result = new KustoExplorerImportResult(
                true,
                [
                    new KustoClusterConnection(new Uri("https://ADX.CONTOSO.COM"), "Duplicate", []),
                    new KustoClusterConnection(
                        new Uri("https://fabrikam.kusto.windows.net"),
                        "Fabrikam",
                        [],
                        "Imported"),
                ],
                1,
                tabs:
                [
                    new KustoExplorerImportedTab(
                        secondTabId,
                        "Second tab",
                        "Events | where Severity > 2",
                        3,
                        1,
                        KustoDocumentTabColor.Purple,
                        new Uri("https://fabrikam.kusto.windows.net"),
                        "Telemetry"),
                    new KustoExplorerImportedTab(
                        firstTabId,
                        "First tab",
                        "Events | where Severity > 2",
                        2,
                        0,
                        KustoDocumentTabColor.Red,
                        new Uri("https://fabrikam.kusto.windows.net"),
                        "Telemetry"),
                ]),
        };
        StubKustoDocumentStore documentStore = new();
        MainWindowViewModel viewModel = CreateViewModel(
            connectionStore: store,
            documentStore: documentStore,
            importService: importService);

        await viewModel.ImportKustoExplorerDataCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Clusters.Count);
        KustoClusterViewModel imported = Assert.Single(
            viewModel.Clusters,
            cluster => cluster.DisplayName == "Fabrikam");
        Assert.Equal("Imported", imported.FolderName);
        KustoDocumentViewModel[] importedTabs = viewModel.Documents
            .Where(document => document.GroupName == "Kusto Explorer tabs")
            .ToArray();
        Assert.Equal(2, importedTabs.Length);
        Assert.Equal([firstTabId, secondTabId], importedTabs.Select(document => document.Id));
        Assert.All(importedTabs, document => Assert.Equal("Events | where Severity > 2", document.Text));
        Assert.Equal(2, importedTabs[0].CaretPosition);
        Assert.Equal(KustoDocumentTabColor.Red, importedTabs[0].TabColor);
        Assert.Equal("Telemetry", importedTabs[0].DatabaseName);
        Assert.Same(importedTabs[0], viewModel.SelectedDocument);
        Assert.Contains("2 skipped", viewModel.StatusText, StringComparison.Ordinal);
        Assert.Equal(2, store.SavedCatalog!.Clusters.Count);

        await viewModel.ImportKustoExplorerDataCommand.ExecuteAsync(null);

        Assert.Equal(
            2,
            viewModel.Documents.Count(document => document.GroupName == "Kusto Explorer tabs"));
        viewModel.Dispose();
        Assert.NotNull(documentStore.SavedWorkspace);
        Assert.Equal(
            2,
            documentStore.SavedWorkspace.Documents.Count(document =>
                document.GroupName == "Kusto Explorer tabs"));
    }

    /// <summary>
    /// Verifies that selecting another database changes the schema delegated to language analysis.
    /// </summary>
    /// <returns>A task that completes after database selection is verified.</returns>
    [Fact]
    public async Task SelectingDatabaseChangesLanguageSchema()
    {
        KustoDatabaseSchema firstSchema = CreateSchema("adx.contoso.com", "First", "FirstTable");
        KustoDatabaseSchema secondSchema = CreateSchema("adx.contoso.com", "Second", "SecondTable");
        KustoConnectionCatalog catalog = new(
            [
                new KustoClusterConnection(
                    new Uri("https://adx.contoso.com"),
                    "Contoso ADX",
                    [
                        new KustoDatabaseConnection("First", "First", firstSchema),
                        new KustoDatabaseConnection("Second", "Second", secondSchema),
                    ]),
            ]);
        StubKustoLanguageService languageService = new();
        MainWindowViewModel viewModel = CreateViewModel(
            languageService: languageService,
            connectionStore: new StubKustoConnectionStore { Catalog = catalog });
        KustoDatabaseViewModel secondDatabase = viewModel.Clusters[0].Databases[1];

        await viewModel.SelectDatabaseCommand.ExecuteAsync(secondDatabase);
        viewModel.Analyze("SecondTable", 4);

        Assert.Equal("Second", languageService.DatabaseSchema?.DatabaseName);
        Assert.Equal("SecondTable", Assert.Single(languageService.DatabaseSchema?.Tables!).Name);
    }

    /// <summary>
    /// Verifies that tab order, selection, text, caret, and database targets restore and flush through autosave.
    /// </summary>
    [Fact]
    public void DocumentsRestoreAndAutosaveCompleteWorkspace()
    {
        Uri clusterUri = new("https://adx.contoso.com");
        KustoDatabaseSchema firstSchema = CreateSchema("adx.contoso.com", "First", "FirstTable");
        KustoDatabaseSchema secondSchema = CreateSchema("adx.contoso.com", "Second", "SecondTable");
        KustoConnectionCatalog catalog = new(
            [
                new KustoClusterConnection(
                    clusterUri,
                    "Contoso ADX",
                    [
                        new KustoDatabaseConnection("First", "First", firstSchema),
                        new KustoDatabaseConnection("Second", "Second", secondSchema),
                    ]),
            ]);
        Guid firstId = Guid.NewGuid();
        Guid secondId = Guid.NewGuid();
        StubKustoDocumentStore documentStore = new()
        {
            Workspace = new KustoDocumentWorkspace(
                [
                    new KustoDocument(firstId, "Investigate", "FirstTable | take 5", 6, clusterUri, "First"),
                    new KustoDocument(secondId, "Monitor", "SecondTable | count", 12, clusterUri, "Second"),
                ],
                secondId),
        };
        MainWindowViewModel viewModel = CreateViewModel(
            connectionStore: new StubKustoConnectionStore { Catalog = catalog },
            documentStore: documentStore);

        Assert.Equal(["Investigate", "Monitor"], viewModel.Documents.Select(document => document.Title));
        Assert.Equal(secondId, viewModel.SelectedDocument?.Id);
        Assert.Equal("SecondTable | count", viewModel.QueryText);
        Assert.Equal(12, viewModel.CaretPosition);
        Assert.Equal("Second", viewModel.DatabaseName);

        viewModel.QueryText = "SecondTable | summarize Total = count()";
        viewModel.CaretPosition = 20;
        viewModel.SelectedDocument = viewModel.Documents[0];
        viewModel.Dispose();

        Assert.NotNull(documentStore.SavedWorkspace);
        Assert.Equal(firstId, documentStore.SavedWorkspace.SelectedDocumentId);
        Assert.Equal(2, documentStore.SavedWorkspace.Documents.Count);
        KustoDocument savedSecond = documentStore.SavedWorkspace.Documents[1];
        Assert.Equal("SecondTable | summarize Total = count()", savedSecond.Text);
        Assert.Equal(20, savedSecond.CaretPosition);
        Assert.Equal(clusterUri, savedSecond.ClusterUri);
        Assert.Equal("Second", savedSecond.DatabaseName);
    }

    /// <summary>
    /// Verifies that deleting text behind the caret cannot expose an invalid autosave snapshot.
    /// </summary>
    [Fact]
    public void TextDeletionClampsCaretBeforeAutosaveNotification()
    {
        StubKustoDocumentStore documentStore = new();
        MainWindowViewModel viewModel = CreateViewModel(documentStore: documentStore);
        viewModel.QueryText = "StormEvents | count";
        viewModel.CaretPosition = viewModel.QueryText.Length;

        viewModel.QueryText = "StormEvents | coun";
        viewModel.Dispose();

        Assert.Equal(viewModel.QueryText.Length, viewModel.CaretPosition);
        Assert.NotNull(documentStore.SavedWorkspace);
        KustoDocument savedDocument = Assert.Single(documentStore.SavedWorkspace.Documents);
        Assert.Equal(savedDocument.Text.Length, savedDocument.CaretPosition);
    }

    /// <summary>
    /// Verifies that each tab retains its own database and Run follows the selected tab target.
    /// </summary>
    /// <returns>A task that completes after both tab targets are executed.</returns>
    [Fact]
    public async Task DocumentTabsRetainIndependentDatabaseTargets()
    {
        Uri clusterUri = new("https://adx.contoso.com");
        KustoConnectionCatalog catalog = new(
            [
                new KustoClusterConnection(
                    clusterUri,
                    "Contoso ADX",
                    [
                        new KustoDatabaseConnection(
                            "First",
                            "First",
                            CreateSchema("adx.contoso.com", "First", "FirstTable")),
                        new KustoDatabaseConnection(
                            "Second",
                            "Second",
                            CreateSchema("adx.contoso.com", "Second", "SecondTable")),
                    ]),
            ]);
        Guid firstId = Guid.NewGuid();
        StubKustoDocumentStore documentStore = new()
        {
            Workspace = new KustoDocumentWorkspace(
                [new KustoDocument(firstId, "First query", "FirstTable | count", 5, clusterUri, "First")],
                firstId),
        };
        StubKustoQueryService queryService = new();
        MainWindowViewModel viewModel = CreateViewModel(
            queryService: queryService,
            connectionStore: new StubKustoConnectionStore { Catalog = catalog },
            documentStore: documentStore);
        KustoDocumentViewModel firstDocument = Assert.Single(viewModel.Documents);
        viewModel.NewQueryCommand.Execute(null);
        KustoDocumentViewModel secondDocument = viewModel.SelectedDocument!;
        KustoDatabaseViewModel secondDatabase = viewModel.Clusters[0].Databases[1];

        await viewModel.SelectDatabaseCommand.ExecuteAsync(secondDatabase);
        viewModel.QueryText = "SecondTable | count";
        await viewModel.RunQueryCommand.ExecuteAsync(null);

        Assert.Equal("Second", queryService.Request?.DatabaseName);
        Assert.Equal("Second", secondDocument.DatabaseName);

        viewModel.SelectedDocument = firstDocument;
        await viewModel.RunQueryCommand.ExecuteAsync(null);

        Assert.Equal("First", queryService.Request?.DatabaseName);
        Assert.Equal("First", viewModel.DatabaseName);
        Assert.Equal("First", firstDocument.DatabaseName);
        Assert.Equal("Second", secondDocument.DatabaseName);
        viewModel.Dispose();
    }

    /// <summary>
    /// Verifies setup persists the caret-selected query, name, recurrence, target, and automatic stop.
    /// </summary>
    [Fact]
    public void ScheduleAutomationCommandPersistsSelectedQuery()
    {
        const string Document = "StormEvents | take 1\n\nStormEvents | count | render timechart";
        string selectedQuery = "StormEvents | count | render timechart";
        int selectedStart = Document.LastIndexOf("StormEvents", StringComparison.Ordinal);
        StubKustoLanguageService languageService = new()
        {
            QuerySelection = new KustoQuerySelection(selectedQuery, selectedStart, selectedQuery.Length),
        };
        StubKustoAutomationStore automationStore = new();
        MainWindowViewModel viewModel = CreateViewModel(
            languageService: languageService,
            automationStore: automationStore);
        viewModel.QueryText = Document;
        viewModel.CaretPosition = Document.Length;

        viewModel.SelectedDocument!.ScheduleCommand.Execute(null);
        viewModel.AutomationName = "Hourly storm count";
        viewModel.AutomationIntervalValue = 2;
        viewModel.AutomationIntervalUnit = KustoAutomationIntervalUnit.Hours;
        viewModel.AutomationStopMode = KustoAutomationStopMode.Days;
        viewModel.AutomationStopAfterValue = 3;
        viewModel.SaveAutomationCommand.Execute(null);

        KustoAutomationViewModel automation = Assert.Single(viewModel.Automations);
        Assert.Equal("Hourly storm count", automation.Name);
        Assert.Equal(selectedQuery, automation.QueryText);
        Assert.Equal(TimeSpan.FromHours(2), automation.Interval);
        Assert.Equal("help.kusto.windows.net / Samples", automation.TargetText);
        Assert.NotNull(automation.StopAtUtc);
        Assert.InRange(
            automation.StopAtUtc.Value - DateTimeOffset.UtcNow,
            TimeSpan.FromDays(2.99),
            TimeSpan.FromDays(3.01));
        Assert.False(viewModel.IsScheduleAutomationOpen);
        Assert.NotNull(automationStore.SavedCatalog);
        Assert.Single(automationStore.SavedCatalog.Automations);
        Assert.True(viewModel.AutomationNotifications.IsOpen);
    }

    /// <summary>
    /// Verifies persisted automation visualization parsing is deferred and cached until run details are inspected.
    /// </summary>
    [Fact]
    public void AutomationVisualizationParsingIsDeferredUntilRunIsInspected()
    {
        DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10);
        KustoAutomation automation = new(
            Guid.NewGuid(),
            "Traffic",
            new Uri("https://adx.contoso.com"),
            "Telemetry",
            "Events | summarize count() by bin(Timestamp, 1h) | render timechart",
            TimeSpan.FromMinutes(5),
            startedAtUtc.AddHours(-1),
            startedAtUtc.AddHours(1),
            null,
            true,
            [CreateSuccessfulAutomationRun(startedAtUtc, 1)]);
        StubKustoLanguageService languageService = new();
        MainWindowViewModel viewModel = CreateViewModel(
            languageService: languageService,
            automationStore: new StubKustoAutomationStore
            {
                Catalog = new KustoAutomationCatalog([automation]),
            });
        KustoAutomationViewModel scheduled = Assert.Single(viewModel.Automations);
        KustoAutomationRunViewModel run = Assert.Single(scheduled.Runs);

        Assert.Null(viewModel.SelectedAutomation);
        Assert.Equal(0, languageService.VisualizationRequestCount);

        viewModel.ShowAutomationsCommand.Execute(null);

        Assert.Same(scheduled, viewModel.SelectedAutomation);
        Assert.Equal(0, languageService.VisualizationRequestCount);

        _ = run.VisualizationMessage;
        _ = run.VisualizationMessage;

        Assert.Equal(1, languageService.VisualizationRequestCount);
    }

    /// <summary>
    /// Verifies an existing automation can be renamed and configured with result actions after creation.
    /// </summary>
    [Fact]
    public void ExistingAutomationSupportsRenameAndNotificationEditing()
    {
        DateTimeOffset utcNow = DateTimeOffset.UtcNow;
        KustoAutomation automation = new(
            Guid.NewGuid(),
            "Original",
            new Uri("https://adx.contoso.com"),
            "Telemetry",
            "Events | take 10",
            TimeSpan.FromMinutes(5),
            utcNow,
            utcNow.AddMinutes(5),
            null,
            true,
            []);
        StubKustoAutomationStore automationStore = new()
        {
            Catalog = new KustoAutomationCatalog([automation]),
        };
        MainWindowViewModel viewModel = CreateViewModel(automationStore: automationStore);
        KustoAutomationViewModel scheduled = Assert.Single(viewModel.Automations);

        scheduled.RenameCommand.Execute(null);
        viewModel.RenameAutomationName = "Renamed";
        viewModel.SaveRenameAutomationCommand.Execute(null);

        scheduled.ConfigureNotificationsCommand.Execute(null);
        viewModel.AutomationNotifications.NotifyWhenRowCountChanges = true;
        viewModel.AutomationNotifications.RowCountComparison =
            KustoAutomationRowCountComparison.GreaterThanOrEqual;
        viewModel.AutomationNotifications.RowCountValue = 5;
        viewModel.AutomationNotifications.DesktopEnabled = true;
        viewModel.AutomationNotifications.RunApplicationEnabled = true;
        viewModel.AutomationNotifications.ApplicationPath = "C:\\Tools\\handle-result.exe";
        viewModel.AutomationNotifications.ApplicationArguments = "--rows {row_count}";
        viewModel.AutomationNotifications.WebhookEnabled = true;
        viewModel.AutomationNotifications.WebhookUsesEnvironmentVariable = true;
        viewModel.AutomationNotifications.WebhookEnvironmentVariableName =
            "OPENKUSTOEXPLORER_OPERATIONS_WEBHOOK";
        viewModel.AutomationNotifications.SaveCommand.Execute(null);

        Assert.Equal("Renamed", scheduled.Name);
        Assert.False(viewModel.IsRenameAutomationOpen);
        Assert.False(viewModel.AutomationNotifications.IsOpen);
        KustoAutomation saved = Assert.Single(automationStore.SavedCatalog!.Automations);
        Assert.Equal("Renamed", saved.Name);
        Assert.True(saved.NotificationSettings.NotifyWhenRowCountChanges);
        Assert.Equal(
            KustoAutomationRowCountComparison.GreaterThanOrEqual,
            saved.NotificationSettings.RowCountComparison);
        Assert.True(saved.NotificationSettings.DesktopEnabled);
        Assert.True(saved.NotificationSettings.RunApplicationEnabled);
        Assert.Equal("C:\\Tools\\handle-result.exe", saved.NotificationSettings.ApplicationPath);
        Assert.Equal("--rows {row_count}", saved.NotificationSettings.ApplicationArguments);
        Assert.Equal(
            KustoAutomationWebhookEndpointSource.EnvironmentVariable,
            saved.NotificationSettings.Webhook?.EndpointSource);
        Assert.Equal(
            "OPENKUSTOEXPLORER_OPERATIONS_WEBHOOK",
            saved.NotificationSettings.Webhook?.EnvironmentVariableName);

        scheduled.ConfigureNotificationsCommand.Execute(null);
        viewModel.AutomationNotifications.WebhookUsesStoredUrl = true;
        viewModel.AutomationNotifications.WebhookStoredUrl = "http://hooks.example.com/automation";
        viewModel.AutomationNotifications.SaveCommand.Execute(null);

        Assert.True(viewModel.AutomationNotifications.IsOpen);
        Assert.Contains("HTTPS", viewModel.AutomationNotifications.ErrorText, StringComparison.Ordinal);

        viewModel.AutomationNotifications.WebhookStoredUrl = "https://hooks.example.com/automation";
        viewModel.AutomationNotifications.SaveCommand.Execute(null);

        saved = Assert.Single(automationStore.SavedCatalog!.Automations);
        Assert.False(viewModel.AutomationNotifications.IsOpen);
        Assert.Equal(
            KustoAutomationWebhookEndpointSource.StoredUrl,
            saved.NotificationSettings.Webhook?.EndpointSource);
        Assert.Equal(
            "https://hooks.example.com/automation",
            saved.NotificationSettings.Webhook?.StoredUrl?.AbsoluteUri.TrimEnd('/'));
    }

    /// <summary>
    /// Verifies the cluster delete command removes and persists the Explorer connection.
    /// </summary>
    [Fact]
    public void ClusterRemoveCommandDeletesPersistedConnection()
    {
        StubKustoConnectionStore connectionStore = new();
        MainWindowViewModel viewModel = CreateViewModel(connectionStore: connectionStore);
        KustoClusterViewModel cluster = Assert.Single(viewModel.Clusters);

        cluster.RemoveCommand.Execute(null);

        Assert.Empty(viewModel.Clusters);
        Assert.NotNull(connectionStore.SavedCatalog);
        Assert.Empty(connectionStore.SavedCatalog.Clusters);
    }

    /// <summary>
    /// Verifies the Automation activity replaces and restores the normal query workbench.
    /// </summary>
    [Fact]
    public void AutomationActivityCommandsSwitchWorkbenchView()
    {
        MainWindowViewModel viewModel = CreateViewModel();

        viewModel.ShowAutomationsCommand.Execute(null);

        Assert.True(viewModel.IsAutomationView);
        Assert.False(viewModel.IsQueryWorkbenchView);

        viewModel.ShowQueryWorkbenchCommand.Execute(null);

        Assert.False(viewModel.IsAutomationView);
        Assert.True(viewModel.IsQueryWorkbenchView);
    }

    /// <summary>
    /// Verifies the Dashboard activity replaces and restores the normal query workbench.
    /// </summary>
    [Fact]
    public void DashboardActivityCommandsSwitchWorkbenchView()
    {
        MainWindowViewModel viewModel = CreateViewModel();

        viewModel.ShowDashboardsCommand.Execute(null);

        Assert.True(viewModel.IsDashboardView);
        Assert.False(viewModel.IsQueryWorkbenchView);

        viewModel.ShowQueryWorkbenchCommand.Execute(null);

        Assert.False(viewModel.IsDashboardView);
        Assert.True(viewModel.IsQueryWorkbenchView);
    }

    /// <summary>
    /// Verifies dashboard pinning carries the caret query, target, and render kind into the widget editor.
    /// </summary>
    [Fact]
    public void PinToDashboardOpensPopulatedWidgetEditor()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        viewModel.QueryText = "StormEvents | summarize Count=count() by bin(StartTime, 1h) | render timechart";
        viewModel.CaretPosition = viewModel.QueryText.Length;

        viewModel.OpenPinToDashboardCommand.Execute(null);

        Assert.True(viewModel.IsDashboardView);
        Assert.Single(viewModel.Dashboard.Dashboards);
        Assert.True(viewModel.Dashboard.IsWidgetEditorOpen);
        Assert.Equal("Samples", viewModel.Dashboard.WidgetEditorDatabaseName);
        Assert.Equal(viewModel.QueryText, viewModel.Dashboard.WidgetEditorQueryText);
        Assert.True(viewModel.Dashboard.WidgetEditorUsesVisualization);
        Assert.Equal(
            KustoVisualizationKind.TimeChart,
            viewModel.Dashboard.SelectedVisualizationOption.Kind);
    }

    /// <summary>
    /// Verifies the global Copilot panel preserves separate Query, Automation, and Graph conversations.
    /// </summary>
    /// <returns>A task that completes after all activity scopes are exercised.</returns>
    [Fact]
    public async Task CopilotPanelSwitchesActivityScopesAndLoadsCypherProposals()
    {
        DateTimeOffset utcNow = new(2026, 7, 24, 18, 0, 0, TimeSpan.Zero);
        KustoAutomation automation = new(
            Guid.NewGuid(),
            "Identity watch",
            new Uri("https://adx.contoso.com"),
            "Security",
            "Events | take 10",
            TimeSpan.FromMinutes(10),
            utcNow,
            utcNow.AddMinutes(10),
            null,
            true,
            []);
        StubKustoCopilotService copilotService = new();
        StubGraphStore graphStore = new()
        {
            State = new GraphStateSummary(
                Guid.NewGuid(),
                "Identity graph",
                "Authentication relationships",
                Guid.NewGuid(),
                utcNow,
                utcNow,
                2,
                1,
                1,
                1),
        };
        MainWindowViewModel viewModel = CreateViewModel(
            automationStore: new StubKustoAutomationStore
            {
                Catalog = new KustoAutomationCatalog([automation]),
            },
            copilotService: copilotService,
            graphStore: graphStore);
        KustoCopilotViewModel queryConversation = viewModel.Copilot;

        viewModel.ToggleCopilotPanelCommand.Execute(null);
        Assert.True(viewModel.IsCopilotPanelOpen);
        Assert.True(queryConversation.IsOpen);

        viewModel.ShowAutomationsCommand.Execute(null);
        KustoCopilotViewModel automationConversation = viewModel.Copilot;
        Assert.NotSame(queryConversation, automationConversation);
        Assert.True(viewModel.IsCopilotPanelOpen);
        Assert.Equal("Test Copilot for Automations", viewModel.CopilotTitle);
        copilotService.Reply = new KustoCopilotReply(
            "Prepared a scheduled query.",
            "Events | summarize count()");
        automationConversation.Prompt = "Explain this schedule";
        await automationConversation.SendCommand.ExecuteAsync(null);
        Assert.Equal(KustoCopilotScopeKind.Automation, copilotService.Context?.ScopeKind);
        Assert.True(automationConversation.HasAutomationProposal);
        Assert.False(automationConversation.HasAppendProposal);
        automationConversation.CreateAutomationCommand.Execute(null);
        Assert.True(viewModel.IsScheduleAutomationOpen);
        Assert.Equal("Events | summarize count()", viewModel.AutomationQueryText);
        Assert.Equal("adx.contoso.com / Security", viewModel.AutomationTargetText);
        viewModel.CloseScheduleAutomationCommand.Execute(null);

        await viewModel.ShowGraphCommand.ExecuteAsync(null);
        KustoCopilotViewModel graphConversation = viewModel.Copilot;
        Assert.NotSame(automationConversation, graphConversation);
        Assert.True(viewModel.IsCopilotPanelOpen);
        Assert.Equal("Test Copilot for Graph", viewModel.CopilotTitle);
        graphConversation.ShareGraphData = true;
        copilotService.Reply = new KustoCopilotReply(
            "Find users.",
            null,
            "MATCH (u:User) RETURN u LIMIT 20");
        graphConversation.Prompt = "Find users in this graph";
        await graphConversation.SendCommand.ExecuteAsync(null);

        Assert.Equal(KustoCopilotScopeKind.Graph, copilotService.Context?.ScopeKind);
        Assert.Equal(graphStore.State.Snapshot, copilotService.Context?.GraphSnapshot);
        Assert.True(copilotService.Options?.ShareGraphData);
        Assert.True(graphConversation.HasCypherProposal);
        Assert.False(graphConversation.HasAppendProposal);
        graphConversation.LoadCypherCommand.Execute(null);
        Assert.Equal("MATCH (u:User) RETURN u LIMIT 20", viewModel.Graph.CypherText);

        viewModel.ShowQueryWorkbenchCommand.Execute(null);
        Assert.Same(queryConversation, viewModel.Copilot);
        Assert.True(viewModel.IsCopilotPanelOpen);
        Assert.Equal("Test Copilot for KQL", viewModel.CopilotTitle);
        Assert.Contains("Explain this schedule", automationConversation.Messages[0].Content, StringComparison.Ordinal);
        Assert.Empty(queryConversation.Messages);
    }

    /// <summary>
    /// Verifies Graph activity refreshes durable counts, supports bounded search, and returns to Query mode.
    /// </summary>
    /// <returns>A task that completes after Graph activity commands run.</returns>
    [Fact]
    public async Task GraphActivityCommandsLoadStateAndSearch()
    {
        DateTimeOffset updatedAt = new(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);
        StubGraphStore graphStore = new()
        {
            State = new GraphStateSummary(Guid.NewGuid(), updatedAt, updatedAt, 2, 1, 1, 1),
            SearchResults =
            [
                new GraphEntitySummary(
                    new GraphEntityKey(GraphEntityKind.User, "User", "alice@example.com"),
                    "Alice",
                    updatedAt,
                    updatedAt,
                    1),
            ],
        };
        graphStore.EntityDetails = new GraphEntityDetails(
            graphStore.SearchResults[0],
            ["AZUser"],
            [new GraphEntityProperty("enabled", "true")],
            1,
            1);
        MainWindowViewModel viewModel = CreateViewModel(graphStore: graphStore);

        await viewModel.ShowGraphCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsGraphView);
        Assert.False(viewModel.IsAutomationView);
        Assert.False(viewModel.IsQueryWorkbenchView);
        Assert.Equal("2 nodes · 1 edge · 1 evidence row", viewModel.Graph.CountText);
        viewModel.Graph.SearchText = "alice";

        await viewModel.Graph.SearchCommand.ExecuteAsync(null);

        Assert.Equal("Alice", Assert.Single(viewModel.Graph.SearchResults).DisplayLabel);
        Assert.Equal(100, graphStore.MaximumSearchResults);
        viewModel.Graph.SelectedEntity = graphStore.SearchResults[0].Entity;
        Assert.Equal("Alice", viewModel.Graph.SelectedEntityDetails?.Summary.DisplayLabel);
        Assert.Equal("AZUser", viewModel.Graph.SelectedEntitySourceLabelsText);
        Assert.Equal("Global", viewModel.Graph.SelectedEntityNamespaceText);
        Assert.Equal(graphStore.SearchResults[0].Entity, graphStore.RequestedEntityDetails);

        viewModel.ShowQueryWorkbenchCommand.Execute(null);

        Assert.True(viewModel.IsQueryWorkbenchView);
        Assert.False(viewModel.IsGraphView);
    }

    /// <summary>
    /// Verifies route commands retain a start node and replace the canvas with only connecting routes.
    /// </summary>
    /// <returns>A task that completes after shortest routes are displayed.</returns>
    [Fact]
    public async Task GraphRouteCommandsDisplayOnlyConnectingSubgraph()
    {
        DateTimeOffset updatedAt = new(2026, 7, 25, 9, 0, 0, TimeSpan.Zero);
        GraphEntitySummary start = CreateGraphSummary(GraphEntityKind.Device, "Device", "start", updatedAt);
        GraphEntitySummary middle = CreateGraphSummary(GraphEntityKind.Device, "Device", "middle", updatedAt);
        GraphEntitySummary destination = CreateGraphSummary(
            GraphEntityKind.Device,
            "Device",
            "destination",
            updatedAt);
        GraphEntitySummary unrelated = CreateGraphSummary(
            GraphEntityKind.Device,
            "Device",
            "unrelated",
            updatedAt);
        GraphRelationshipKey[] routeRelationships =
        [
            new(start.Entity, middle.Entity, "Connects"),
            new(middle.Entity, destination.Entity, "Connects"),
        ];
        GraphViewport overview = new(
            start.Entity,
            [start, middle, destination, unrelated],
            routeRelationships,
            false);
        GraphViewport routeViewport = new(
            start.Entity,
            [start, middle, destination],
            routeRelationships,
            false);
        StubGraphStore graphStore = new()
        {
            State = new GraphStateSummary(Guid.NewGuid(), updatedAt, updatedAt, 4, 2, 1, 1),
            SearchResults = [destination],
            ViewportFactory = (_, _, _) => overview,
            NeighborhoodFactory = (_, _, _, _) => overview,
            RouteFactory = (_, _, _, _, _) => Task.FromResult(
                new GraphRouteResult(start.Entity, destination.Entity, 2, routeViewport)),
        };
        MainWindowViewModel viewModel = CreateViewModel(graphStore: graphStore);
        await viewModel.ShowGraphCommand.ExecuteAsync(null);

        viewModel.Graph.SetRouteStartCommand.Execute(start.Entity);
        viewModel.Graph.SearchText = "destination";
        await viewModel.Graph.SearchCommand.ExecuteAsync(null);

        Assert.Equal(start.Entity, viewModel.Graph.RouteStart);
        Assert.Equal(destination.Entity, viewModel.Graph.SelectedEntity);
        await viewModel.Graph.ShowRoutesToNodeCommand.ExecuteAsync(viewModel.Graph.SelectedEntity);

        Assert.Equal(start.Entity, graphStore.RequestedRouteStart);
        Assert.Equal(destination.Entity, graphStore.RequestedRouteDestination);
        Assert.Equal(3, viewModel.Graph.Layout?.Nodes.Count);
        Assert.DoesNotContain(viewModel.Graph.Layout!.Nodes, node => node.Entity.Entity == unrelated.Entity);
        Assert.Contains(start.Entity, viewModel.Graph.SelectedEntities);
        Assert.Contains(destination.Entity, viewModel.Graph.SelectedEntities);
        Assert.Equal("2 hops · 3 route nodes · 2 route edges", viewModel.Graph.StatusText);
        Assert.True(viewModel.Graph.CanShowOverview);
    }

    /// <summary>
    /// Verifies an in-flight route search exposes progress and propagates cancellation.
    /// </summary>
    /// <returns>A task that completes after route cancellation is observed.</returns>
    [Fact]
    public async Task GraphRouteSearchCanBeCanceled()
    {
        DateTimeOffset updatedAt = new(2026, 7, 25, 9, 30, 0, TimeSpan.Zero);
        GraphEntitySummary start = CreateGraphSummary(GraphEntityKind.Device, "Device", "start", updatedAt);
        GraphEntitySummary destination = CreateGraphSummary(
            GraphEntityKind.Device,
            "Device",
            "destination",
            updatedAt);
        GraphViewport overview = new(start.Entity, [start, destination], [], false);
        TaskCompletionSource<bool> routeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        StubGraphStore graphStore = new()
        {
            State = new GraphStateSummary(Guid.NewGuid(), updatedAt, updatedAt, 2, 0, 1, 1),
            ViewportFactory = (_, _, _) => overview,
            RouteFactory = async (_, _, _, _, cancellationToken) =>
            {
                routeStarted.SetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new GraphRouteResult(start.Entity, destination.Entity, null, overview);
            },
        };
        MainWindowViewModel viewModel = CreateViewModel(graphStore: graphStore);
        await viewModel.ShowGraphCommand.ExecuteAsync(null);
        viewModel.Graph.SetRouteStartCommand.Execute(start.Entity);

        Task routeTask = viewModel.Graph.ShowRoutesToNodeCommand.ExecuteAsync(destination.Entity);
        await routeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(viewModel.Graph.IsFindingRoutes);
        Assert.True(viewModel.Graph.IsBusy);
        Assert.True(viewModel.Graph.CancelRouteCommand.CanExecute(null));
        viewModel.Graph.CancelRouteCommand.Execute(null);
        await routeTask;

        Assert.False(viewModel.Graph.IsFindingRoutes);
        Assert.False(viewModel.Graph.IsBusy);
        Assert.Equal("Route search canceled", viewModel.Graph.StatusText);
    }

    /// <summary>
    /// Verifies clearing the canvas selection does not reenter its collection change notification.
    /// </summary>
    [Fact]
    public void GraphActivitySelectionCanBeClearedFromTheCanvas()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        GraphEntityKey entity = new(GraphEntityKind.User, "User", "alice@example.com");
        viewModel.Graph.SelectedEntity = entity;

        viewModel.Graph.SelectedEntities.Clear();

        Assert.Null(viewModel.Graph.SelectedEntity);
        Assert.Empty(viewModel.Graph.SelectedEntities);
    }

    /// <summary>
    /// Verifies opening and refreshing a live graph includes every node in its overview.
    /// </summary>
    /// <returns>A task that completes after the initial load and data refresh.</returns>
    [Fact]
    public async Task GraphActivityShowsAllNodesAfterOpeningAndAddingData()
    {
        DateTimeOffset updatedAt = new(2026, 7, 27, 8, 0, 0, TimeSpan.Zero);
        GraphEntitySummary[] entities = Enumerable.Range(1, 601)
            .Select(index => new GraphEntitySummary(
                new GraphEntityKey(GraphEntityKind.Device, "Device", $"device-{index}"),
                $"Device {index}",
                updatedAt,
                updatedAt,
                0))
            .ToArray();
        StubGraphStore graphStore = new()
        {
            State = new GraphStateSummary(Guid.NewGuid(), updatedAt, updatedAt, 600, 0, 1, 600),
        };
        graphStore.ViewportFactory = (_, maximumEntityCount, _) => new GraphViewport(
            null,
            entities.Take(maximumEntityCount).ToArray(),
            [],
            maximumEntityCount < graphStore.State.EntityCount);
        MainWindowViewModel viewModel = CreateViewModel(graphStore: graphStore);

        await viewModel.ShowGraphCommand.ExecuteAsync(null);

        Assert.Equal(600, viewModel.Graph.Layout?.Nodes.Count);
        Assert.Equal(600, graphStore.MaximumViewportEntityCount);

        graphStore.State = new GraphStateSummary(
            graphStore.State.GraphId,
            graphStore.State.GraphName,
            graphStore.State.GraphDescription,
            graphStore.State.GenerationId,
            graphStore.State.CreatedAtUtc,
            updatedAt.AddMinutes(1),
            601,
            0,
            2,
            601);

        await viewModel.Graph.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(601, viewModel.Graph.Layout?.Nodes.Count);
        Assert.Equal(601, graphStore.MaximumViewportEntityCount);
    }

    /// <summary>
    /// Verifies a context node's visible label can be edited and rendered without changing its identity.
    /// </summary>
    /// <returns>A task that completes after the label is persisted and the layout updates.</returns>
    [Fact]
    public async Task GraphContextNodeVisibleLabelCanBeChanged()
    {
        const string UpdatedLabel = "Primary investigation account";
        DateTimeOffset updatedAt = new(2026, 7, 27, 13, 0, 0, TimeSpan.Zero);
        GraphEntityKey entity = new(GraphEntityKind.User, "User", "alice@example.com");
        GraphEntitySummary original = new(entity, "Alice", updatedAt, updatedAt, 0);
        StubGraphStore graphStore = new()
        {
            State = new GraphStateSummary(Guid.NewGuid(), updatedAt, updatedAt, 1, 0, 1, 1),
            ViewportFactory = (center, maximumEntityCount, maximumRelationshipCount) =>
            {
                _ = maximumEntityCount;
                _ = maximumRelationshipCount;
                return new GraphViewport(center, [original], [], false);
            },
        };
        MainWindowViewModel viewModel = CreateViewModel(graphStore: graphStore);
        await viewModel.ShowGraphCommand.ExecuteAsync(null);

        viewModel.Graph.OpenNodeLabelEditorCommand.Execute(entity);

        Assert.True(viewModel.Graph.IsNodeLabelEditorOpen);
        Assert.Equal("Alice", viewModel.Graph.NodeLabelText);
        viewModel.Graph.NodeLabelText = UpdatedLabel;
        await viewModel.Graph.SaveNodeLabelCommand.ExecuteAsync(null);

        Assert.Equal(entity, graphStore.RequestedDisplayLabelEntity);
        Assert.Equal(UpdatedLabel, graphStore.RequestedDisplayLabel);
        Assert.False(viewModel.Graph.IsNodeLabelEditorOpen);
        GraphLayoutNode renderedNode = Assert.Single(viewModel.Graph.Layout!.Nodes);
        Assert.Equal(entity, renderedNode.Entity.Entity);
        Assert.Equal(UpdatedLabel, renderedNode.Entity.DisplayLabel);
    }

    /// <summary>
    /// Verifies a complete overview is restored after focusing one graph neighborhood.
    /// </summary>
    /// <returns>A task that completes after focused navigation and overview restoration.</returns>
    [Fact]
    public async Task GraphActivityRestoresCompleteOverviewAfterFocusedNavigation()
    {
        DateTimeOffset updatedAt = new(2026, 7, 24, 8, 0, 0, TimeSpan.Zero);
        GraphEntitySummary[] entities = Enumerable.Range(1, 600)
            .Select(index => new GraphEntitySummary(
                new GraphEntityKey(GraphEntityKind.Device, "Device", $"device-{index}"),
                $"Device {index}",
                updatedAt,
                updatedAt,
                0))
            .ToArray();
        StubGraphStore graphStore = new()
        {
            State = new GraphStateSummary(Guid.NewGuid(), updatedAt, updatedAt, 600, 0, 1, 600),
            ViewportFactory = (center, maximumEntityCount, maximumRelationshipCount) =>
            {
                _ = maximumRelationshipCount;
                GraphEntitySummary[] visibleEntities = center is null
                    ? entities.Take(maximumEntityCount).ToArray()
                    : [entities.Single(entity => entity.Entity == center)];
                return new GraphViewport(
                    center ?? visibleEntities[0].Entity,
                    visibleEntities,
                    [],
                    visibleEntities.Length < entities.Length);
            },
            NeighborhoodFactory = (centers, maximumDepth, maximumEntityCount, maximumRelationshipCount) =>
            {
                _ = maximumDepth;
                _ = maximumEntityCount;
                _ = maximumRelationshipCount;
                GraphEntitySummary entity = entities.Single(candidate => candidate.Entity == centers.Single());
                return new GraphViewport(entity.Entity, [entity], [], true);
            },
        };
        MainWindowViewModel viewModel = CreateViewModel(graphStore: graphStore);

        await viewModel.ShowGraphCommand.ExecuteAsync(null);

        Assert.Equal(600, viewModel.Graph.Layout?.Nodes.Count);
        Assert.Equal(0, viewModel.Graph.HiddenEntityCount);
        Assert.False(viewModel.Graph.HasHiddenItems);
        Assert.False(viewModel.Graph.CanShowMore);
        Assert.Equal(600, graphStore.MaximumViewportEntityCount);

        await viewModel.Graph.LoadViewportCommand.ExecuteAsync(entities[599]);

        Assert.Equal(599, viewModel.Graph.HiddenEntityCount);
        Assert.True(viewModel.Graph.CanShowOverview);
        Assert.Equal([entities[599].Entity], graphStore.RequestedNeighborhoodCenters);

        await viewModel.Graph.ShowOverviewCommand.ExecuteAsync(null);

        Assert.Null(graphStore.RequestedViewportCenter);
        Assert.Equal(600, viewModel.Graph.Layout?.Nodes.Count);
        Assert.Equal(0, viewModel.Graph.HiddenEntityCount);
        Assert.False(viewModel.Graph.CanShowOverview);
    }

    /// <summary>
    /// Verifies search, depth, multi-selection, legend, context expansion, filters, and prune compose.
    /// </summary>
    /// <returns>A task that completes after graph view transformations.</returns>
    [Fact]
    public async Task GraphInteractionCommandsComposeAcrossSelectedNodes()
    {
        DateTimeOffset updatedAt = new(2026, 7, 24, 9, 0, 0, TimeSpan.Zero);
        GraphEntitySummary firstTeam = CreateGraphSummary(
            GraphEntityKind.Group,
            "GitHubTeam",
            "github:HildeTeamTNT",
            updatedAt);
        GraphEntitySummary secondTeam = CreateGraphSummary(
            GraphEntityKind.Group,
            "GitHubTeam",
            "github:OtherTeamTNT",
            updatedAt);
        GraphEntitySummary repository = CreateGraphSummary(
            GraphEntityKind.CloudResource,
            "Repository",
            "repo:security",
            updatedAt);
        GraphEntitySummary upstreamUser = CreateGraphSummary(
            GraphEntityKind.User,
            "GitHubUser",
            "github:hilde",
            updatedAt);
        GraphEntitySummary unrelatedHost = CreateGraphSummary(
            GraphEntityKind.Host,
            "Host",
            "host:isolated",
            updatedAt);
        GraphEntitySummary[] entities =
        [
            firstTeam,
            secondTeam,
            repository,
            upstreamUser,
            unrelatedHost,
        ];
        GraphRelationshipKey[] relationships =
        [
            new(upstreamUser.Entity, firstTeam.Entity, "MemberOf", "user-team"),
            new(firstTeam.Entity, repository.Entity, "Owns", "first-repo"),
            new(secondTeam.Entity, repository.Entity, "Owns", "second-repo"),
        ];
        GraphViewport fullViewport = new(firstTeam.Entity, entities, relationships, false);
        StubGraphStore graphStore = new()
        {
            State = new GraphStateSummary(Guid.NewGuid(), updatedAt, updatedAt, 5, 3, 1, 8),
            SearchResults = [firstTeam, secondTeam],
            ViewportFactory = (center, maximumEntityCount, maximumRelationshipCount) =>
            {
                _ = center;
                _ = maximumEntityCount;
                _ = maximumRelationshipCount;
                return fullViewport;
            },
            NeighborhoodFactory = (centers, maximumDepth, maximumEntityCount, maximumRelationshipCount) =>
            {
                _ = centers;
                _ = maximumDepth;
                _ = maximumEntityCount;
                _ = maximumRelationshipCount;
                return fullViewport;
            },
        };
        MainWindowViewModel viewModel = CreateViewModel(graphStore: graphStore);

        await viewModel.ShowGraphCommand.ExecuteAsync(null);

        GraphTypeLegendItem teamLegend = Assert.Single(
            viewModel.Graph.LegendItems,
            item => item.TypeName == "GitHubTeam");
        Assert.Equal(2, teamLegend.NodeCount);
        viewModel.Graph.SearchText = "TeamTNT";

        await viewModel.Graph.SearchCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Graph.SelectedEntities.Count);
        Assert.Contains(firstTeam.Entity, viewModel.Graph.SelectedEntities);
        Assert.Contains(secondTeam.Entity, viewModel.Graph.SelectedEntities);
        Assert.Equal(1, graphStore.RequestedNeighborhoodDepth);
        Assert.Equal(2, graphStore.RequestedNeighborhoodCenters.Length);

        viewModel.Graph.NeighborhoodDepth = 3;
        await viewModel.Graph.ReloadNeighborhoodCommand.ExecuteAsync(null);

        Assert.Equal(3, graphStore.RequestedNeighborhoodDepth);
        await viewModel.Graph.ExpandNodeCommand.ExecuteAsync(upstreamUser.Entity);
        Assert.Equal(4, graphStore.RequestedNeighborhoodDepth);
        Assert.Contains(upstreamUser.Entity, graphStore.RequestedNeighborhoodCenters);

        await viewModel.Graph.KeepConnectedCommand.ExecuteAsync(null);

        Assert.Equal(4, viewModel.Graph.Layout?.Nodes.Count);
        Assert.DoesNotContain(
            viewModel.Graph.Layout!.Nodes,
            node => node.Entity.Entity == unrelatedHost.Entity);
        Assert.True(viewModel.Graph.HasViewFilters);

        await viewModel.Graph.ResetFiltersCommand.ExecuteAsync(null);

        Assert.Equal(5, viewModel.Graph.Layout?.Nodes.Count);
        await viewModel.Graph.PruneUpstreamCommand.ExecuteAsync(repository.Entity);
        GraphLayoutNode remainingNode = Assert.Single(viewModel.Graph.Layout!.Nodes);
        Assert.Equal(unrelatedHost.Entity, remainingNode.Entity.Entity);
    }

    /// <summary>
    /// Verifies switching saved graphs activates and renders only the selected graph snapshot.
    /// </summary>
    /// <returns>A task that completes after both graph snapshots are loaded.</returns>
    [Fact]
    public async Task GraphCatalogSelectionSwitchesTheRenderedSnapshot()
    {
        DateTimeOffset updatedAt = new(2026, 7, 24, 15, 0, 0, TimeSpan.Zero);
        GraphEntitySummary firstEntity = new(
            new GraphEntityKey(GraphEntityKind.User, "User", "first-user"),
            "First user",
            updatedAt,
            updatedAt,
            0);
        GraphEntitySummary secondEntity = new(
            new GraphEntityKey(GraphEntityKind.Host, "Host", "second-host"),
            "Second host",
            updatedAt,
            updatedAt,
            0);
        GraphStateSummary firstState = new(
            Guid.NewGuid(),
            "First graph",
            "First description",
            Guid.NewGuid(),
            updatedAt,
            updatedAt,
            1,
            0,
            1,
            1);
        GraphStateSummary secondState = new(
            Guid.NewGuid(),
            "Second graph",
            "Second description",
            Guid.NewGuid(),
            updatedAt,
            updatedAt,
            1,
            0,
            1,
            1);
        GraphCatalogEntry firstEntry = new(
            firstState.GraphId,
            firstState.GraphName,
            firstState.GraphDescription,
            firstState.GenerationId,
            firstState.CreatedAtUtc,
            firstState.LastUpdatedAtUtc,
            firstState.LastUpdatedAtUtc,
            firstState.EntityCount,
            firstState.RelationshipCount);
        GraphCatalogEntry secondEntry = new(
            secondState.GraphId,
            secondState.GraphName,
            secondState.GraphDescription,
            secondState.GenerationId,
            secondState.CreatedAtUtc,
            secondState.LastUpdatedAtUtc,
            secondState.LastUpdatedAtUtc,
            secondState.EntityCount,
            secondState.RelationshipCount);
        StubGraphStore graphStore = new()
        {
            State = firstState,
            Catalog = new GraphCatalog(firstState.GraphId, [firstEntry, secondEntry]),
        };
        graphStore.ViewportFactory = (_, _, _) => new GraphViewport(
            null,
            graphStore.State.GraphId == firstState.GraphId ? [firstEntity] : [secondEntity],
            [],
            false);

        MainWindowViewModel viewModel = CreateViewModel(graphStore: graphStore);
        await viewModel.ShowGraphCommand.ExecuteAsync(null);

        Assert.Equal("First graph", viewModel.Graph.SelectedGraph?.Name);
        Assert.Equal(firstState.Snapshot, graphStore.RequestedSnapshot);
        Assert.Contains(viewModel.Graph.Layout!.Nodes, node => node.Entity.Entity == firstEntity.Entity);

        viewModel.Graph.SelectedGraph = viewModel.Graph.Graphs.Single(graph => graph.GraphId == secondState.GraphId);
        await viewModel.Graph.ActivateGraphCommand.ExecutionTask!;

        Assert.Equal("Second graph", viewModel.Graph.SelectedGraph?.Name);
        Assert.Equal(secondState.Snapshot, graphStore.RequestedSnapshot);
        Assert.DoesNotContain(viewModel.Graph.Layout!.Nodes, node => node.Entity.Entity == firstEntity.Entity);
        Assert.Contains(viewModel.Graph.Layout.Nodes, node => node.Entity.Entity == secondEntity.Entity);
    }

    /// <summary>
    /// Verifies graph metadata commands create, edit, and delete saved graphs explicitly.
    /// </summary>
    /// <returns>A task that completes after the catalog workflow is exercised.</returns>
    [Fact]
    public async Task GraphCatalogCommandsManageNamesAndDescriptions()
    {
        StubGraphStore graphStore = new();
        MainWindowViewModel viewModel = CreateViewModel(graphStore: graphStore);
        await viewModel.ShowGraphCommand.ExecuteAsync(null);

        viewModel.Graph.OpenCreateGraphCommand.Execute(null);
        Assert.True(viewModel.Graph.IsGraphEditorOpen);
        Assert.False(viewModel.Graph.CanSaveGraph);
        viewModel.Graph.GraphEditorName = "Threat response";
        viewModel.Graph.GraphEditorDescription = "Privileged identity investigation";
        await viewModel.Graph.SaveGraphCommand.ExecuteAsync(null);

        Assert.False(viewModel.Graph.IsGraphEditorOpen);
        Assert.Equal(2, viewModel.Graph.Graphs.Count);
        Assert.Equal("Threat response", viewModel.Graph.SelectedGraph?.Name);
        Assert.Equal("Privileged identity investigation", viewModel.Graph.GraphDescriptionText);

        viewModel.Graph.OpenEditGraphCommand.Execute(null);
        viewModel.Graph.GraphEditorName = "Identity response";
        viewModel.Graph.GraphEditorDescription = "Updated scope";
        await viewModel.Graph.SaveGraphCommand.ExecuteAsync(null);

        Assert.Equal("Identity response", viewModel.Graph.SelectedGraph?.Name);
        Assert.Equal("Updated scope", viewModel.Graph.GraphDescriptionText);
        Assert.True(viewModel.Graph.CanDeleteGraph);

        viewModel.Graph.OpenDeleteGraphCommand.Execute(null);
        Assert.True(viewModel.Graph.IsDeleteGraphOpen);
        await viewModel.Graph.DeleteGraphCommand.ExecuteAsync(null);

        Assert.False(viewModel.Graph.IsDeleteGraphOpen);
        Assert.Single(viewModel.Graph.Graphs);
        Assert.Equal("Default graph", viewModel.Graph.SelectedGraph?.Name);
        Assert.False(viewModel.Graph.CanDeleteGraph);
    }

    /// <summary>
    /// Verifies all saved graphs can be permanently replaced with one fresh empty default after confirmation.
    /// </summary>
    /// <returns>A task that completes after the graph catalog is reset.</returns>
    [Fact]
    public async Task GraphDeleteAllCommandResetsCatalogAfterConfirmation()
    {
        StubGraphStore graphStore = new();
        MainWindowViewModel viewModel = CreateViewModel(graphStore: graphStore);
        await viewModel.ShowGraphCommand.ExecuteAsync(null);

        foreach (string graphName in new[] { "Old graph one", "Old graph two" })
        {
            viewModel.Graph.OpenCreateGraphCommand.Execute(null);
            viewModel.Graph.GraphEditorName = graphName;
            await viewModel.Graph.SaveGraphCommand.ExecuteAsync(null);
        }

        Assert.Equal(3, viewModel.Graph.Graphs.Count);
        Assert.Equal("3 saved graphs", viewModel.Graph.DeleteAllGraphsSummary);

        viewModel.Graph.OpenDeleteAllGraphsCommand.Execute(null);
        Assert.True(viewModel.Graph.IsDeleteAllGraphsOpen);
        await viewModel.Graph.DeleteAllGraphsCommand.ExecuteAsync(null);

        GraphCatalogEntry remaining = Assert.Single(viewModel.Graph.Graphs);
        Assert.Equal("Default graph", remaining.Name);
        Assert.Equal(remaining.GraphId, viewModel.Graph.SelectedGraph?.GraphId);
        Assert.True(viewModel.Graph.State?.IsEmpty);
        Assert.False(viewModel.Graph.IsDeleteAllGraphsOpen);
        Assert.Equal("All saved graphs deleted", viewModel.Graph.StatusText);
    }

    /// <summary>
    /// Verifies clearing replaces only the selected graph generation after confirmation.
    /// </summary>
    /// <returns>A task that completes after graph clearing.</returns>
    [Fact]
    public async Task GraphClearCommandStartsAnEmptyGeneration()
    {
        DateTimeOffset updatedAt = DateTimeOffset.UtcNow;
        GraphStateSummary initialState = new(
            Guid.NewGuid(),
            "Incident graph",
            "Retained investigation",
            Guid.NewGuid(),
            updatedAt.AddMinutes(-5),
            updatedAt,
            12,
            8,
            2,
            20);
        StubGraphStore graphStore = new()
        {
            State = initialState,
        };
        MainWindowViewModel viewModel = CreateViewModel(graphStore: graphStore);
        await viewModel.ShowGraphCommand.ExecuteAsync(null);

        Assert.True(viewModel.Graph.CanClearGraph);
        viewModel.Graph.OpenClearGraphCommand.Execute(null);
        Assert.True(viewModel.Graph.IsClearGraphOpen);

        await viewModel.Graph.ClearGraphCommand.ExecuteAsync(null);

        Assert.Equal(initialState.Snapshot, graphStore.RequestedSnapshot);
        Assert.False(viewModel.Graph.IsClearGraphOpen);
        Assert.True(viewModel.Graph.State?.IsEmpty);
        Assert.NotEqual(initialState.GenerationId, viewModel.Graph.State?.GenerationId);
        Assert.Equal(initialState.GraphId, viewModel.Graph.State?.GraphId);
        Assert.Single(viewModel.Graph.Graphs);
        Assert.False(viewModel.Graph.CanClearGraph);
    }

    /// <summary>
    /// Verifies the Graph history selector displays retained state and returns to the live graph.
    /// </summary>
    /// <returns>A task that completes after both timeline selections load.</returns>
    [Fact]
    public async Task GraphTimelineSwitchesBetweenHistoricalAndLiveState()
    {
        DateTimeOffset currentAt = new(2026, 7, 24, 10, 0, 0, TimeSpan.Zero);
        DateTimeOffset historicalAt = currentAt.AddHours(-1);
        GraphStateSummary state = new(
            Guid.NewGuid(),
            "Incident graph",
            string.Empty,
            Guid.NewGuid(),
            historicalAt,
            currentAt,
            1,
            0,
            1,
            1);
        GraphEntitySummary liveEntity = CreateGraphSummary(
            GraphEntityKind.Host,
            "Host",
            "live-host",
            currentAt);
        GraphEntitySummary historicalEntity = CreateGraphSummary(
            GraphEntityKind.User,
            "User",
            "historical-user",
            historicalAt);
        GraphTimelinePoint historicalPoint = new(
            new GraphSnapshot(state.GraphId, Guid.NewGuid()),
            historicalAt,
            Guid.NewGuid(),
            GraphIngestionSourceKind.ManualQuery,
            "09:00 investigation");
        StubGraphStore graphStore = new()
        {
            State = state,
            SearchResults = [liveEntity],
            Timeline = [historicalPoint],
            TimelineViewport = new GraphViewport(
                historicalEntity.Entity,
                [historicalEntity],
                [],
                false),
            ViewportFactory = (_, _, _) => new GraphViewport(
                liveEntity.Entity,
                [liveEntity],
                [],
                false),
        };
        MainWindowViewModel viewModel = CreateViewModel(graphStore: graphStore);
        await viewModel.ShowGraphCommand.ExecuteAsync(null);
        GraphTimelinePointViewModel liveSelection = viewModel.Graph.TimelinePoints[0];
        GraphTimelinePointViewModel historicalSelection = viewModel.Graph.TimelinePoints[1];

        Assert.True(liveSelection.IsLive);
        Assert.False(viewModel.Graph.IsViewingHistory);
        viewModel.Graph.SelectedTimelinePoint = historicalSelection;
        await viewModel.Graph.ViewTimelinePointCommand.ExecutionTask!;

        Assert.True(viewModel.Graph.IsViewingHistory);
        Assert.Equal(historicalPoint, graphStore.RequestedTimelinePoint);
        Assert.Contains(viewModel.Graph.Layout!.Nodes, node => node.Entity.Entity == historicalEntity.Entity);
        Assert.DoesNotContain(viewModel.Graph.Layout.Nodes, node => node.Entity.Entity == liveEntity.Entity);
        Assert.False(viewModel.Graph.CanRunCypher);
        Assert.False(viewModel.Graph.CanClearGraph);
        Assert.Contains("Historical view", viewModel.Graph.CountText, StringComparison.Ordinal);

        viewModel.Graph.SelectedTimelinePoint = liveSelection;
        await viewModel.Graph.ViewTimelinePointCommand.ExecutionTask!;

        Assert.False(viewModel.Graph.IsViewingHistory);
        Assert.Contains(viewModel.Graph.Layout!.Nodes, node => node.Entity.Entity == liveEntity.Entity);
    }

    /// <summary>
    /// Verifies openCypher updates typed rows and the canvas only after successful execution.
    /// </summary>
    /// <returns>A task that completes after successful and invalid query results are applied.</returns>
    [Fact]
    public async Task OpenCypherUpdatesCanvasRowsAndPreservesSuccessOnErrors()
    {
        DateTimeOffset updatedAt = new(2026, 7, 24, 17, 0, 0, TimeSpan.Zero);
        GraphEntitySummary overviewEntity = new(
            new GraphEntityKey(GraphEntityKind.User, "User", "overview-user"),
            "Overview user",
            updatedAt,
            updatedAt,
            0);
        GraphEntitySummary matchedEntity = new(
            new GraphEntityKey(GraphEntityKind.Host, "Host", "matched-host"),
            "Matched host",
            updatedAt,
            updatedAt,
            0);
        GraphStateSummary state = new(
            Guid.NewGuid(),
            "Cypher graph",
            string.Empty,
            Guid.NewGuid(),
            updatedAt,
            updatedAt,
            2,
            0,
            1,
            1);
        GraphQueryResult success = new(
            state.Snapshot,
            "MATCH (n:Host) RETURN n",
            [new GraphQueryColumn("n", GraphQueryValueKind.Entity)],
            [new GraphQueryRow([GraphQueryValue.FromEntity(matchedEntity.Entity)])],
            new GraphViewport(matchedEntity.Entity, [matchedEntity], [], false),
            [],
            TimeSpan.FromMilliseconds(4),
            false);
        StubGraphStore graphStore = new()
        {
            State = state,
            QueryResult = success,
            ViewportFactory = (_, _, _) => new GraphViewport(
                overviewEntity.Entity,
                [overviewEntity],
                [],
                false),
        };
        MainWindowViewModel viewModel = CreateViewModel(graphStore: graphStore);
        await viewModel.ShowGraphCommand.ExecuteAsync(null);
        viewModel.Graph.CypherText = success.QueryText;

        await viewModel.Graph.RunCypherCommand.ExecuteAsync(null);

        Assert.True(viewModel.Graph.HasCypherRows);
        GraphQueryRowViewModel row = Assert.Single(viewModel.Graph.CypherRows);
        GraphQueryCellViewModel cell = Assert.Single(row.Cells);
        Assert.Equal(matchedEntity.Entity, cell.Entity);
        Assert.Contains(viewModel.Graph.Layout!.Nodes, node => node.Entity.Entity == matchedEntity.Entity);
        Assert.DoesNotContain(viewModel.Graph.Layout.Nodes, node => node.Entity.Entity == overviewEntity.Entity);
        cell.SelectCommand.Execute(null);
        Assert.Equal(matchedEntity.Entity, viewModel.Graph.SelectedEntity);

        graphStore.QueryResult = new GraphQueryResult(
            state.Snapshot,
            "MATCH (n DELETE n",
            [],
            [],
            new GraphViewport(null, [], [], false),
            [new GraphQueryDiagnostic(GraphQueryDiagnosticSeverity.Error, 9, 6, "Expected ')'.")],
            TimeSpan.FromMilliseconds(1),
            false);
        viewModel.Graph.CypherText = graphStore.QueryResult.QueryText;
        await viewModel.Graph.RunCypherCommand.ExecuteAsync(null);

        Assert.True(viewModel.Graph.HasCypherDiagnostics);
        Assert.Contains(viewModel.Graph.Layout!.Nodes, node => node.Entity.Entity == matchedEntity.Entity);
        Assert.Equal("OpenCypher has errors", viewModel.Graph.CypherResultSummary);
    }

    /// <summary>
    /// Verifies selecting a relationship result loads bounded provenance into the graph inspector.
    /// </summary>
    /// <returns>A task that completes after relationship details load.</returns>
    [Fact]
    public async Task OpenCypherRelationshipSelectionLoadsEvidence()
    {
        DateTimeOffset updatedAt = new(2026, 7, 24, 17, 30, 0, TimeSpan.Zero);
        GraphEntitySummary source = CreateGraphSummary(
            GraphEntityKind.User,
            "User",
            "alice@example.com",
            updatedAt);
        GraphEntitySummary target = CreateGraphSummary(
            GraphEntityKind.Host,
            "Host",
            "server-1",
            updatedAt);
        GraphRelationshipKey relationship = new(source.Entity, target.Entity, "AuthenticatedTo");
        GraphStateSummary state = new(
            Guid.NewGuid(),
            "Evidence graph",
            string.Empty,
            Guid.NewGuid(),
            updatedAt,
            updatedAt,
            2,
            1,
            1,
            1);
        GraphEvidenceRecord evidence = new(
            Guid.NewGuid(),
            GraphIngestionSourceKind.ManualQuery,
            Guid.NewGuid(),
            "Sign-in query",
            new Uri("https://adx.contoso.com"),
            "Security",
            "Events | make-graph User --> Host",
            updatedAt,
            updatedAt,
            "Graph edges",
            0,
            "{\"columns\":[]}",
            "{\"user\":\"alice@example.com\",\"host\":\"server-1\"}");
        GraphRelationshipDetails details = new(
            relationship,
            ["SIGN_IN"],
            [new GraphEntityProperty("method", "MFA")],
            updatedAt,
            updatedAt,
            1,
            1,
            [evidence]);
        StubGraphStore graphStore = new()
        {
            State = state,
            RelationshipDetails = details,
            QueryResult = new GraphQueryResult(
                state.Snapshot,
                "MATCH ()-[r]->() RETURN r",
                [new GraphQueryColumn("r", GraphQueryValueKind.Relationship)],
                [new GraphQueryRow([GraphQueryValue.FromRelationship(relationship)])],
                new GraphViewport(source.Entity, [source, target], [relationship], false),
                [],
                TimeSpan.FromMilliseconds(3),
                false),
        };
        MainWindowViewModel viewModel = CreateViewModel(graphStore: graphStore);
        await viewModel.ShowGraphCommand.ExecuteAsync(null);

        await viewModel.Graph.RunCypherCommand.ExecuteAsync(null);
        GraphQueryCellViewModel cell = Assert.Single(Assert.Single(viewModel.Graph.CypherRows).Cells);
        cell.SelectCommand.Execute(null);

        Assert.Equal(relationship, viewModel.Graph.SelectedRelationship);
        Assert.Equal(state.Snapshot, graphStore.RequestedSnapshot);
        Assert.Equal(relationship, graphStore.RequestedRelationshipDetails);
        Assert.Same(details, viewModel.Graph.SelectedRelationshipDetails);
        Assert.Empty(viewModel.Graph.SelectedEntities);
        Assert.Equal("Relationship evidence", viewModel.Graph.InspectorTitle, ignoreCase: true);
        Assert.Contains("1 evidence row", viewModel.Graph.SelectedRelationshipEvidenceSummary, StringComparison.Ordinal);
        Assert.Equal(evidence.RowJson, details.Evidence[0].RowJson);
    }

    /// <summary>
    /// Verifies the automation list shows a concise relative next-run countdown.
    /// </summary>
    /// <returns>A task that completes after the scheduler refreshes display time.</returns>
    [Fact]
    public async Task AutomationNextRunTextUsesHumanReadableCountdown()
    {
        DateTimeOffset utcNow = new(2026, 7, 22, 10, 0, 0, TimeSpan.Zero);
        KustoAutomation automation = new(
            Guid.NewGuid(),
            "Traffic",
            new Uri("https://adx.contoso.com"),
            "Telemetry",
            "Events | count",
            TimeSpan.FromMinutes(5),
            utcNow.AddHours(-1),
            utcNow.AddMinutes(5),
            null,
            true,
            []);
        StubKustoAutomationStore automationStore = new()
        {
            Catalog = new KustoAutomationCatalog([automation]),
        };
        MainWindowViewModel viewModel = CreateViewModel(automationStore: automationStore);

        await viewModel.RunDueAutomationsAsync(utcNow);

        Assert.Equal("Next in 5m", Assert.Single(viewModel.Automations).NextRunText);
    }

    /// <summary>
    /// Verifies a due automation executes, persists history, and reuses table and visualization outputs.
    /// </summary>
    /// <returns>A task that completes after the scheduled query runs.</returns>
    [Fact]
    public async Task RunDueAutomationsExecutesAndPersistsVisualizedHistory()
    {
        DateTimeOffset utcNow = DateTimeOffset.UtcNow;
        KustoAutomation automation = new(
            Guid.NewGuid(),
            "Traffic",
            new Uri("https://adx.contoso.com"),
            "Telemetry",
            "Events | summarize Events=count() by bin(Timestamp, 1h) | render timechart",
            TimeSpan.FromMinutes(5),
            utcNow.AddHours(-1),
            utcNow.AddSeconds(-1),
            null,
            true,
            []);
        StubKustoAutomationStore automationStore = new()
        {
            Catalog = new KustoAutomationCatalog([automation]),
        };
        KustoResultTable table = new(
            "PrimaryResult",
            [new KustoResultColumn("Timestamp", "datetime"), new KustoResultColumn("Events", "long")],
            [new KustoResultRow(["2026-07-22T10:00:00Z", "42"])]);
        StubKustoQueryService queryService = new()
        {
            Result = new KustoQueryResult([table], TimeSpan.FromMilliseconds(20)),
        };
        MainWindowViewModel viewModel = CreateViewModel(
            queryService: queryService,
            automationStore: automationStore);

        await viewModel.RunDueAutomationsAsync(utcNow);

        Assert.NotNull(queryService.Request);
        Assert.Equal("Telemetry", queryService.Request.DatabaseName);
        KustoAutomationViewModel scheduled = Assert.Single(viewModel.Automations);
        KustoAutomationRunViewModel run = Assert.Single(scheduled.Runs);
        Assert.Equal(KustoAutomationRunStatus.Succeeded, run.Status);
        Assert.True(run.HasResultTable);
        Assert.True(run.HasVisualization);
        Assert.False(run.ShowVisualizationChoices);
        Assert.Equal(KustoVisualizationKind.TimeChart, run.Visualization?.Kind);
        Assert.Equal("42", Assert.Single(run.ResultRows).Cells[1].Text);
        Assert.NotNull(automationStore.SavedCatalog);
        Assert.Single(Assert.Single(automationStore.SavedCatalog.Automations).Runs);
    }

    /// <summary>
    /// Verifies scheduled graph queries add to the graph captured before remote execution.
    /// </summary>
    /// <returns>A task that completes after the scheduled graph import.</returns>
    [Fact]
    public async Task RunDueAutomationsImportsGraphQueriesAdditively()
    {
        const string Query = "graph(\"SecurityGraph\")";
        DateTimeOffset utcNow = DateTimeOffset.UtcNow;
        KustoAutomation automation = new(
            Guid.NewGuid(),
            "Security graph",
            new Uri("https://adx.contoso.com"),
            "Security",
            Query,
            TimeSpan.FromMinutes(5),
            utcNow.AddHours(-1),
            utcNow.AddSeconds(-1),
            null,
            true,
            []);
        StubKustoLanguageService languageService = new()
        {
            GraphQueryPlan = CreateGraphQueryPlan(Query),
        };
        StubKustoQueryService queryService = new();
        StubKustoGraphIngestionService graphIngestionService = new();
        StubGraphStore graphStore = new();
        GraphSnapshot expectedSnapshot = graphStore.State.Snapshot;
        MainWindowViewModel viewModel = CreateViewModel(
            languageService: languageService,
            queryService: queryService,
            automationStore: new StubKustoAutomationStore
            {
                Catalog = new KustoAutomationCatalog([automation]),
            },
            graphIngestionService: graphIngestionService,
            graphStore: graphStore);

        await viewModel.RunDueAutomationsAsync(utcNow);

        Assert.Null(queryService.Request);
        KustoGraphIngestionRequest request = Assert.IsType<KustoGraphIngestionRequest>(
            graphIngestionService.Request);
        Assert.Equal(GraphImportMode.Add, request.ImportMode);
        Assert.Equal(GraphIngestionSourceKind.Automation, request.SourceKind);
        Assert.Equal(automation.Id, request.SourceId);
        Assert.Equal(automation.Name, request.SourceName);
        Assert.Equal(expectedSnapshot, request.Target?.Snapshot);
        KustoAutomationRunViewModel run = Assert.Single(Assert.Single(viewModel.Automations).Runs);
        Assert.Equal(KustoAutomationRunStatus.Succeeded, run.Status);
        Assert.True(run.HasResultTable);
    }

    /// <summary>
    /// Verifies automation history exposes signed row changes with semantic colors.
    /// </summary>
    [Fact]
    public void AutomationHistoryShowsSignedRowDeltas()
    {
        DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow.AddHours(-1);
        KustoAutomation automation = new(
            Guid.NewGuid(),
            "Traffic",
            new Uri("https://adx.contoso.com"),
            "Telemetry",
            "Events | count",
            TimeSpan.FromMinutes(5),
            startedAtUtc.AddHours(-1),
            startedAtUtc.AddHours(1),
            null,
            true,
            [
                CreateSuccessfulAutomationRun(startedAtUtc, 4),
                CreateSuccessfulAutomationRun(startedAtUtc.AddMinutes(5), 6),
                CreateSuccessfulAutomationRun(startedAtUtc.AddMinutes(10), 3),
                CreateSuccessfulAutomationRun(startedAtUtc.AddMinutes(15), 3),
            ]);
        StubKustoAutomationStore automationStore = new()
        {
            Catalog = new KustoAutomationCatalog([automation]),
        };
        MainWindowViewModel viewModel = CreateViewModel(automationStore: automationStore);
        KustoAutomationViewModel scheduled = Assert.Single(viewModel.Automations);

        Assert.Equal("0 rows", scheduled.Runs[0].RowDeltaText);
        Assert.Equal("#6B7280", scheduled.Runs[0].RowDeltaColorHex);
        Assert.Equal("-3 rows", scheduled.Runs[1].RowDeltaText);
        Assert.Equal("#D64545", scheduled.Runs[1].RowDeltaColorHex);
        Assert.Equal("+2 rows", scheduled.Runs[2].RowDeltaText);
        Assert.Equal("#27864A", scheduled.Runs[2].RowDeltaColorHex);
        Assert.False(scheduled.Runs[3].HasRowDelta);
    }

    /// <summary>
    /// Verifies a changed row count raises a formatted notification after a successful run.
    /// </summary>
    /// <returns>A task that completes after notification evaluation.</returns>
    [Fact]
    public async Task AutomationRowChangeRaisesFormattedNotification()
    {
        DateTimeOffset utcNow = DateTimeOffset.UtcNow;
        KustoAutomationNotificationSettings settings = new(
            true,
            KustoAutomationRowCountComparison.None,
            0,
            true,
            false,
            null,
            null,
            null,
            587,
            true,
            "{name}: {row_count}",
            "{rows_changed} rows\n{query}");
        KustoAutomation automation = new(
            Guid.NewGuid(),
            "Traffic",
            new Uri("https://adx.contoso.com"),
            "Telemetry",
            "Events | take 10",
            TimeSpan.FromMinutes(5),
            utcNow.AddHours(-1),
            utcNow.AddSeconds(-1),
            null,
            true,
            [CreateSuccessfulAutomationRun(utcNow.AddMinutes(-5), 4)],
            settings);
        StubKustoAutomationStore automationStore = new()
        {
            Catalog = new KustoAutomationCatalog([automation]),
        };
        KustoResultTable table = new(
            "PrimaryResult",
            [new KustoResultColumn("Value", "long")],
            Enumerable.Range(0, 6).Select(index => new KustoResultRow([$"{index}"])));
        MainWindowViewModel viewModel = CreateViewModel(
            queryService: new StubKustoQueryService
            {
                Result = new KustoQueryResult([table], TimeSpan.FromMilliseconds(10)),
            },
            automationStore: automationStore);
        KustoAutomationNotification? notification = null;
        viewModel.AutomationNotificationRequested += (_, eventArguments) =>
            notification = eventArguments.Notification;

        await viewModel.RunDueAutomationsAsync(utcNow);

        Assert.NotNull(notification);
        Assert.Equal("Traffic: 6", notification.Title);
        Assert.Equal(6, notification.RowCount);
        Assert.Equal(2, notification.RowsChanged);
        Assert.Contains("+2 rows", notification.Message, StringComparison.Ordinal);
        Assert.Contains("Events | take 10", notification.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a run missed before an elapsed stop deadline executes once and then pauses.
    /// </summary>
    /// <returns>A task that completes after the scheduler tick.</returns>
    [Fact]
    public async Task RunDueAutomationsExecutesMissedRunBeforeDisablingExpiredSchedule()
    {
        DateTimeOffset utcNow = DateTimeOffset.UtcNow;
        KustoAutomation automation = new(
            Guid.NewGuid(),
            "Expired",
            new Uri("https://adx.contoso.com"),
            "Telemetry",
            "Events | count",
            TimeSpan.FromMinutes(5),
            utcNow.AddHours(-2),
            utcNow.AddMinutes(-10),
            utcNow.AddMinutes(-1),
            true,
            []);
        StubKustoAutomationStore automationStore = new()
        {
            Catalog = new KustoAutomationCatalog([automation]),
        };
        StubKustoQueryService queryService = new();
        MainWindowViewModel viewModel = CreateViewModel(
            queryService: queryService,
            automationStore: automationStore);

        await viewModel.RunDueAutomationsAsync(utcNow);

        Assert.NotNull(queryService.Request);
        KustoAutomationViewModel scheduled = Assert.Single(viewModel.Automations);
        Assert.False(scheduled.IsEnabled);
        Assert.Single(scheduled.Runs);
        KustoAutomation saved = Assert.Single(automationStore.SavedCatalog!.Automations);
        Assert.False(saved.IsEnabled);
        Assert.Single(saved.Runs);
    }

    /// <summary>
    /// Verifies an occurrence scheduled after the stop deadline is disabled without running.
    /// </summary>
    /// <returns>A task that completes after the scheduler tick.</returns>
    [Fact]
    public async Task RunDueAutomationsSkipsOccurrenceAfterStopDeadline()
    {
        DateTimeOffset utcNow = DateTimeOffset.UtcNow;
        KustoAutomation automation = new(
            Guid.NewGuid(),
            "Expired",
            new Uri("https://adx.contoso.com"),
            "Telemetry",
            "Events | count",
            TimeSpan.FromMinutes(5),
            utcNow.AddHours(-2),
            utcNow.AddMinutes(-5),
            utcNow.AddMinutes(-10),
            true,
            []);
        StubKustoAutomationStore automationStore = new()
        {
            Catalog = new KustoAutomationCatalog([automation]),
        };
        StubKustoQueryService queryService = new();
        MainWindowViewModel viewModel = CreateViewModel(
            queryService: queryService,
            automationStore: automationStore);

        await viewModel.RunDueAutomationsAsync(utcNow);

        Assert.Null(queryService.Request);
        Assert.False(Assert.Single(viewModel.Automations).IsEnabled);
    }

    private static KustoDatabaseSchema CreateSchema(string clusterName, string databaseName, string tableName)
    {
        KustoTableSchema table = new(tableName, [new KustoColumnSchema("Timestamp", KustoScalarType.DateTime)]);
        return new KustoDatabaseSchema(clusterName, databaseName, [table]);
    }

    private static KustoGraphQueryPlan CreateGraphQueryPlan(string queryText)
    {
        return new KustoGraphQueryPlan(
            new KustoQuerySelection(queryText, 0, queryText.Length),
            KustoGraphSourceKind.GraphFunction,
            "export query",
            "nodes_export",
            "edges_export",
            "node_hash",
            "source_hash",
            "target_hash",
            null,
            null);
    }

    private static KustoAutomationRun CreateSuccessfulAutomationRun(
        DateTimeOffset startedAtUtc,
        int rowCount)
    {
        KustoResultTable table = new(
            "PrimaryResult",
            [new KustoResultColumn("Value", "long")],
            Enumerable.Range(0, rowCount).Select(index => new KustoResultRow([$"{index}"])));
        KustoQueryResult result = new([table], TimeSpan.FromMilliseconds(10));
        return new KustoAutomationRun(
            Guid.NewGuid(),
            startedAtUtc,
            startedAtUtc.AddMilliseconds(10),
            KustoAutomationRunStatus.Succeeded,
            null,
            result);
    }

    private static KustoClusterConnection CreateConnection(
        string clusterAddress,
        string displayName,
        string? folderName)
    {
        Uri clusterUri = new(clusterAddress);
        string databaseName = $"{displayName}Database";
        KustoDatabaseSchema schema = CreateSchema(clusterUri.Host, databaseName, $"{displayName}Table");
        KustoDatabaseConnection database = new(databaseName, databaseName, schema);
        return new KustoClusterConnection(clusterUri, displayName, [database], folderName);
    }

    private static GraphEntitySummary CreateGraphSummary(
        GraphEntityKind kind,
        string typeName,
        string canonicalId,
        DateTimeOffset updatedAt)
    {
        GraphEntityKey entity = new(kind, typeName, canonicalId);
        return new GraphEntitySummary(entity, canonicalId, updatedAt, updatedAt, 1);
    }

    private static MainWindowViewModel CreateViewModel(
        StubKustoLanguageService? languageService = null,
        StubKustoQueryService? queryService = null,
        StubKustoCatalogService? catalogService = null,
        StubKustoConnectionStore? connectionStore = null,
        StubKustoDocumentStore? documentStore = null,
        StubKustoDashboardStore? dashboardStore = null,
        StubKustoAutomationStore? automationStore = null,
        StubKustoExplorerImportService? importService = null,
        StubKustoCopilotService? copilotService = null,
        StubKustoGraphIngestionService? graphIngestionService = null,
        StubGraphStore? graphStore = null,
        StubGraphLayoutService? graphLayoutService = null,
        IKustoRecordedSessionStore? recordedSessionStore = null,
        IKustoPredicateInterestExtractor? predicateInterestExtractor = null,
        IKustoRecordedRelationExtractor? recordedRelationExtractor = null,
        IKustoRecordedChainSearcher? recordedChainSearcher = null,
        IKustoRecordedRelationPlanner? recordedRelationPlanner = null,
        IKustoRecordedChainQueryGenerator? recordedChainGenerator = null)
    {
        return new MainWindowViewModel(
            languageService ?? new StubKustoLanguageService(),
            queryService ?? new StubKustoQueryService(),
            catalogService ?? new StubKustoCatalogService(),
            connectionStore ?? new StubKustoConnectionStore(),
            documentStore ?? new StubKustoDocumentStore(),
            dashboardStore ?? new StubKustoDashboardStore(),
            automationStore ?? new StubKustoAutomationStore(),
            importService ?? new StubKustoExplorerImportService(),
            copilotService ?? new StubKustoCopilotService(),
            graphIngestionService ?? new StubKustoGraphIngestionService(),
            graphStore ?? new StubGraphStore(),
            graphLayoutService ?? new StubGraphLayoutService(),
            recordedSessionStore,
            predicateInterestExtractor,
            recordedRelationExtractor,
            recordedChainSearcher,
            recordedRelationPlanner,
            recordedChainGenerator);
    }

    private sealed class StubKustoLanguageService : IKustoLanguageService
    {
        public StubKustoLanguageService()
        {
            Analysis = new KustoLanguageAnalysis([], [], [], 0, 0);
        }

        public KustoLanguageAnalysis Analysis { get; }

        public Func<string, KustoLanguageAnalysis>? AnalysisFactory { get; init; }

        public string? Text { get; private set; }

        public int CaretPosition { get; private set; }

        public KustoDatabaseSchema? DatabaseSchema { get; private set; }

        public KustoQuerySelection? QuerySelection { get; init; }

        public KustoGraphQueryPlan? GraphQueryPlan { get; init; }

        public int VisualizationRequestCount { get; private set; }

        public string? SelectionText { get; private set; }

        public int SelectionCaretPosition { get; private set; }

        public KustoQuerySelection? GetQueryAtPosition(string text, int caretPosition)
        {
            SelectionText = text;
            SelectionCaretPosition = caretPosition;
            KustoQuerySelection? selection = QuerySelection;

            if (selection is null && !string.IsNullOrWhiteSpace(text))
            {
                selection = new KustoQuerySelection(text, 0, text.Length);
            }

            return selection;
        }

        public KustoGraphQueryPlan? GetGraphQueryPlanAtPosition(string text, int caretPosition)
        {
            return GraphQueryPlan;
        }

        public KustoVisualization? GetVisualizationAtPosition(string text, int caretPosition)
        {
            VisualizationRequestCount++;
            return text.Contains("render timechart", StringComparison.OrdinalIgnoreCase)
                ? new KustoVisualization(KustoVisualizationKind.TimeChart)
                : null;
        }

        public KustoSyntaxHelp? GetSyntaxHelp(
            string text,
            int position,
            KustoDatabaseSchema databaseSchema,
            CancellationToken cancellationToken = default)
        {
            return null;
        }

        public KustoLanguageAnalysis Analyze(
            string text,
            int caretPosition,
            KustoDatabaseSchema databaseSchema,
            CancellationToken cancellationToken = default)
        {
            Text = text;
            CaretPosition = caretPosition;
            DatabaseSchema = databaseSchema;

            return AnalysisFactory?.Invoke(text) ?? Analysis;
        }
    }

    private sealed class StubKustoQueryService : IKustoQueryService
    {
        public StubKustoQueryService()
        {
            Result = new KustoQueryResult([], TimeSpan.Zero);
        }

        public Exception? Failure { get; init; }

        public int ExecuteCount { get; private set; }

        public KustoQueryRequest? Request { get; private set; }

        public KustoQueryResult Result { get; init; }

        public Task<KustoQueryResult> ExecuteAsync(
            KustoQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            ExecuteCount++;
            Request = request;
            Task<KustoQueryResult> task = Failure is null
                ? Task.FromResult(Result)
                : Task.FromException<KustoQueryResult>(Failure);

            return task;
        }
    }

    private sealed class StubGraphStore : IGraphStore, IGraphQueryService
    {
        public StubGraphStore()
        {
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;
            State = new GraphStateSummary(Guid.NewGuid(), utcNow, utcNow, 0, 0, 0, 0);
        }

        public int MaximumSearchResults { get; private set; }

        public int MaximumViewportEntityCount { get; private set; }

        public int MaximumViewportRelationshipCount { get; private set; }

        public int RequestedNeighborhoodDepth { get; private set; }

        public GraphEntityKey[] RequestedNeighborhoodCenters { get; private set; } = [];

        public GraphEntityKey? RequestedViewportCenter { get; private set; }

        public GraphSnapshot? RequestedSnapshot { get; private set; }

        public GraphEntityDetails? EntityDetails { get; set; }

        public GraphRelationshipDetails? RelationshipDetails { get; set; }

        public GraphEntityKey? RequestedEntityDetails { get; private set; }

        public GraphEntityKey? RequestedDisplayLabelEntity { get; private set; }

        public string? RequestedDisplayLabel { get; private set; }

        public GraphRelationshipKey? RequestedRelationshipDetails { get; private set; }

        public GraphEntityKey? RequestedRouteStart { get; private set; }

        public GraphEntityKey? RequestedRouteDestination { get; private set; }

        public IReadOnlyList<GraphEntitySummary> SearchResults { get; init; } = [];

        public Func<GraphEntityKey?, int, int, GraphViewport>? ViewportFactory { get; set; }

        public Func<IReadOnlyCollection<GraphEntityKey>, int, int, int, GraphViewport>? NeighborhoodFactory { get; init; }

        public Func<GraphEntityKey, GraphEntityKey, int, int, CancellationToken, Task<GraphRouteResult>>? RouteFactory { get; init; }

        public GraphStateSummary State { get; set; }

        public GraphCatalog? Catalog { get; set; }

        public GraphQueryResult? QueryResult { get; set; }

        public IReadOnlyList<GraphTimelinePoint> Timeline { get; init; } = [];

        public GraphViewport? TimelineViewport { get; init; }

        public GraphTimelinePoint? RequestedTimelinePoint { get; private set; }

        public Task<GraphStateSummary> ClearAsync(CancellationToken cancellationToken = default)
        {
            return ClearAsync(new GraphWriteTarget(State.Snapshot), cancellationToken);
        }

        public Task<GraphStateSummary> ClearAsync(
            GraphWriteTarget target,
            CancellationToken cancellationToken = default)
        {
            RequestedSnapshot = target.Snapshot;
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;
            State = new GraphStateSummary(
                State.GraphId,
                State.GraphName,
                State.GraphDescription,
                Guid.NewGuid(),
                utcNow,
                utcNow,
                0,
                0,
                0,
                0);
            GraphCatalog current = Catalog ?? CreateCatalog(new GraphStateSummary(
                target.Snapshot.GraphId,
                State.GraphName,
                State.GraphDescription,
                target.Snapshot.GenerationId,
                utcNow,
                utcNow,
                0,
                0,
                0,
                0));
            GraphCatalogEntry replacement = CreateCatalogEntry(State, State.GraphName, State.GraphDescription);
            Catalog = new GraphCatalog(
                State.GraphId,
                current.Graphs.Select(graph => graph.GraphId == State.GraphId ? replacement : graph));
            return Task.FromResult(State);
        }

        public Task<GraphCatalog> GetCatalogAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Catalog ?? CreateCatalog(State));
        }

        public Task<GraphStateSummary> CreateGraphAsync(
            string name,
            string? description,
            CancellationToken cancellationToken = default)
        {
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;
            State = new GraphStateSummary(
                Guid.NewGuid(),
                name,
                description,
                Guid.NewGuid(),
                utcNow,
                utcNow,
                0,
                0,
                0,
                0);
            GraphCatalogEntry newEntry = CreateCatalogEntry(State, State.GraphName, State.GraphDescription);
            GraphCatalog existingCatalog = Catalog ?? CreateCatalog(new GraphStateSummary(
                Guid.NewGuid(),
                utcNow,
                utcNow,
                0,
                0,
                0,
                0));
            Catalog = new GraphCatalog(
                State.GraphId,
                existingCatalog.Graphs.Append(newEntry));
            return Task.FromResult(State);
        }

        public Task<GraphCatalogEntry> UpdateGraphAsync(
            Guid graphId,
            string name,
            string? description,
            CancellationToken cancellationToken = default)
        {
            GraphCatalogEntry entry = CreateCatalogEntry(State, name, description);
            GraphCatalog current = Catalog ?? CreateCatalog(State);
            GraphCatalogEntry[] graphs = current.Graphs
                .Select(graph => graph.GraphId == graphId ? entry : graph)
                .ToArray();
            Catalog = new GraphCatalog(current.ActiveGraphId, graphs);
            State = new GraphStateSummary(
                entry.GraphId,
                entry.Name,
                entry.Description,
                entry.GenerationId,
                entry.CreatedAtUtc,
                entry.LastUpdatedAtUtc,
                entry.EntityCount,
                entry.RelationshipCount,
                State.IngestionCount,
                State.EvidenceCount);
            return Task.FromResult(entry);
        }

        public Task<GraphStateSummary> ActivateGraphAsync(
            Guid graphId,
            CancellationToken cancellationToken = default)
        {
            GraphCatalog catalog = Catalog ?? CreateCatalog(State);
            GraphCatalogEntry entry = catalog.Graphs.Single(graph => graph.GraphId == graphId);
            State = new GraphStateSummary(
                entry.GraphId,
                entry.Name,
                entry.Description,
                entry.GenerationId,
                entry.CreatedAtUtc,
                entry.LastUpdatedAtUtc,
                entry.EntityCount,
                entry.RelationshipCount,
                0,
                0);
            Catalog = new GraphCatalog(graphId, catalog.Graphs);
            return Task.FromResult(State);
        }

        public Task<GraphCatalog> DeleteGraphAsync(
            Guid graphId,
            CancellationToken cancellationToken = default)
        {
            GraphCatalog catalog = Catalog ?? CreateCatalog(State);
            GraphCatalogEntry[] remaining = catalog.Graphs.Where(graph => graph.GraphId != graphId).ToArray();
            GraphCatalogEntry active = remaining[0];
            Catalog = new GraphCatalog(active.GraphId, remaining);
            return Task.FromResult(Catalog);
        }

        public Task<GraphCatalog> DeleteAllGraphsAsync(CancellationToken cancellationToken = default)
        {
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;
            State = new GraphStateSummary(Guid.NewGuid(), utcNow, utcNow, 0, 0, 0, 0);
            Catalog = CreateCatalog(State);
            return Task.FromResult(Catalog);
        }

        public Task<GraphStateSummary> GetStateAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(State);
        }

        public Task<GraphStateSummary> GetStateAsync(
            GraphSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            RequestedSnapshot = snapshot;
            return Task.FromResult(State);
        }

        public Task<GraphEntitySummary> SetEntityDisplayLabelAsync(
            GraphSnapshot snapshot,
            GraphEntityKey entity,
            string displayLabel,
            CancellationToken cancellationToken = default)
        {
            RequestedSnapshot = snapshot;
            RequestedDisplayLabelEntity = entity;
            RequestedDisplayLabel = displayLabel;
            cancellationToken.ThrowIfCancellationRequested();
            GraphEntitySummary existing = SearchResults.FirstOrDefault(result => result.Entity == entity)
                ?? new GraphEntitySummary(entity, displayLabel, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0);
            return Task.FromResult(new GraphEntitySummary(
                entity,
                displayLabel,
                existing.FirstDiscoveredAtUtc,
                existing.LastUpdatedAtUtc,
                existing.Degree));
        }

        public Task<IReadOnlyList<GraphEntityIdentityConflict>> FindIdentityConflictsAsync(
            GraphSnapshot snapshot,
            IEnumerable<GraphEntityIdentityCandidate> candidates,
            CancellationToken cancellationToken = default)
        {
            _ = candidates;
            RequestedSnapshot = snapshot;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<GraphEntityIdentityConflict>>([]);
        }

        public Task<GraphQuerySchema> GetSchemaAsync(
            GraphSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            RequestedSnapshot = snapshot;
            return Task.FromResult(new GraphQuerySchema(snapshot, [], []));
        }

        public Task<GraphQueryResult> ExecuteOpenCypherAsync(
            GraphQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            RequestedSnapshot = request.Snapshot;
            GraphQueryResult result = QueryResult ?? new GraphQueryResult(
                request.Snapshot,
                request.QueryText,
                [],
                [],
                new GraphViewport(null, [], [], false),
                [],
                TimeSpan.Zero,
                false);
            return Task.FromResult(result);
        }

        public Task<GraphEntityDetails?> GetEntityDetailsAsync(
            GraphEntityKey entity,
            CancellationToken cancellationToken = default)
        {
            RequestedEntityDetails = entity;
            return Task.FromResult(EntityDetails);
        }

        public Task<GraphEntityDetails?> GetEntityDetailsAsync(
            GraphSnapshot snapshot,
            GraphEntityKey entity,
            CancellationToken cancellationToken = default)
        {
            RequestedSnapshot = snapshot;
            return GetEntityDetailsAsync(entity, cancellationToken);
        }

        public Task<GraphRelationshipDetails?> GetRelationshipDetailsAsync(
            GraphRelationshipKey relationship,
            CancellationToken cancellationToken = default)
        {
            RequestedRelationshipDetails = relationship;
            return Task.FromResult(RelationshipDetails);
        }

        public Task<GraphRelationshipDetails?> GetRelationshipDetailsAsync(
            GraphSnapshot snapshot,
            GraphRelationshipKey relationship,
            CancellationToken cancellationToken = default)
        {
            RequestedSnapshot = snapshot;
            RequestedRelationshipDetails = relationship;
            return Task.FromResult(RelationshipDetails);
        }

        public Task<IReadOnlyList<GraphTimelinePoint>> GetTimelineAsync(
            Guid graphId,
            int maximumPoints,
            CancellationToken cancellationToken = default)
        {
            _ = graphId;
            return Task.FromResult<IReadOnlyList<GraphTimelinePoint>>(Timeline.Take(maximumPoints).ToArray());
        }

        public Task<GraphViewport> GetTimelineViewportAsync(
            GraphTimelinePoint point,
            int maximumEntityCount,
            int maximumRelationshipCount,
            CancellationToken cancellationToken = default)
        {
            _ = maximumEntityCount;
            _ = maximumRelationshipCount;
            RequestedTimelinePoint = point;
            return Task.FromResult(TimelineViewport ?? new GraphViewport(null, [], [], false));
        }

        public Task<GraphViewport> GetViewportAsync(
            GraphEntityKey? center,
            int maximumEntityCount,
            int maximumRelationshipCount,
            CancellationToken cancellationToken = default)
        {
            RequestedViewportCenter = center;
            MaximumViewportEntityCount = maximumEntityCount;
            MaximumViewportRelationshipCount = maximumRelationshipCount;
            GraphViewport viewport = ViewportFactory?.Invoke(
                center,
                maximumEntityCount,
                maximumRelationshipCount) ?? new GraphViewport(null, [], [], false);
            return Task.FromResult(viewport);
        }

        public Task<GraphViewport> GetViewportAsync(
            GraphSnapshot snapshot,
            GraphEntityKey? center,
            int maximumEntityCount,
            int maximumRelationshipCount,
            CancellationToken cancellationToken = default)
        {
            RequestedSnapshot = snapshot;
            return GetViewportAsync(
                center,
                maximumEntityCount,
                maximumRelationshipCount,
                cancellationToken);
        }

        public Task<GraphViewport> GetNeighborhoodAsync(
            IReadOnlyCollection<GraphEntityKey> centers,
            int maximumDepth,
            int maximumEntityCount,
            int maximumRelationshipCount,
            CancellationToken cancellationToken = default)
        {
            RequestedNeighborhoodCenters = centers.ToArray();
            RequestedNeighborhoodDepth = maximumDepth;
            MaximumViewportEntityCount = maximumEntityCount;
            MaximumViewportRelationshipCount = maximumRelationshipCount;
            GraphViewport viewport = NeighborhoodFactory?.Invoke(
                centers,
                maximumDepth,
                maximumEntityCount,
                maximumRelationshipCount) ?? new GraphViewport(null, [], [], false);
            return Task.FromResult(viewport);
        }

        public Task<GraphViewport> GetNeighborhoodAsync(
            GraphSnapshot snapshot,
            IReadOnlyCollection<GraphEntityKey> centers,
            int maximumDepth,
            int maximumEntityCount,
            int maximumRelationshipCount,
            CancellationToken cancellationToken = default)
        {
            RequestedSnapshot = snapshot;
            return GetNeighborhoodAsync(
                centers,
                maximumDepth,
                maximumEntityCount,
                maximumRelationshipCount,
                cancellationToken);
        }

        public Task<GraphRouteResult> FindRoutesAsync(
            GraphSnapshot snapshot,
            GraphEntityKey start,
            GraphEntityKey destination,
            int maximumEntityCount,
            int maximumRelationshipCount,
            CancellationToken cancellationToken = default)
        {
            RequestedSnapshot = snapshot;
            RequestedRouteStart = start;
            RequestedRouteDestination = destination;
            return RouteFactory?.Invoke(
                start,
                destination,
                maximumEntityCount,
                maximumRelationshipCount,
                cancellationToken) ?? Task.FromException<GraphRouteResult>(new NotSupportedException());
        }

        public Task<GraphImportResult> ImportAsync(
            IGraphImportSource source,
            GraphImportMode mode,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<GraphImportResult>(new NotSupportedException());
        }

        public Task<GraphImportResult> ImportAsync(
            GraphWriteTarget target,
            IGraphImportSource source,
            GraphImportMode mode,
            CancellationToken cancellationToken = default)
        {
            RequestedSnapshot = target.Snapshot;
            return ImportAsync(source, mode, cancellationToken);
        }

        public Task<IReadOnlyList<GraphEntitySummary>> SearchEntitiesAsync(
            string searchText,
            int maximumResults,
            CancellationToken cancellationToken = default)
        {
            _ = searchText;
            MaximumSearchResults = maximumResults;
            return Task.FromResult(SearchResults);
        }

        public Task<IReadOnlyList<GraphEntitySummary>> SearchEntitiesAsync(
            GraphSnapshot snapshot,
            string searchText,
            int maximumResults,
            CancellationToken cancellationToken = default)
        {
            RequestedSnapshot = snapshot;
            return SearchEntitiesAsync(searchText, maximumResults, cancellationToken);
        }

        private static GraphCatalog CreateCatalog(GraphStateSummary state)
        {
            GraphCatalogEntry entry = CreateCatalogEntry(state, state.GraphName, state.GraphDescription);
            return new GraphCatalog(state.GraphId, [entry]);
        }

        private static GraphCatalogEntry CreateCatalogEntry(
            GraphStateSummary state,
            string name,
            string? description)
        {
            return new GraphCatalogEntry(
                state.GraphId,
                name,
                description,
                state.GenerationId,
                state.CreatedAtUtc,
                state.LastUpdatedAtUtc,
                state.LastUpdatedAtUtc,
                state.EntityCount,
                state.RelationshipCount);
        }
    }

    private sealed class StubGraphLayoutService : IGraphLayoutService
    {
        public Task<GraphLayout> LayoutAsync(
            GraphViewport viewport,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GraphLayoutNode[] nodes = viewport.Entities
                .Select((entity, index) => new GraphLayoutNode(
                    entity,
                    new GraphLayoutPoint(index * 100, 50),
                    80,
                    32,
                    entity.Entity == viewport.Center))
                .ToArray();
            double width = nodes.Length == 0 ? 0 : nodes[^1].Center.X + 50;
            double height = nodes.Length == 0 ? 0 : 100;
            return Task.FromResult(new GraphLayout(
                width,
                height,
                nodes,
                [],
                viewport.IsTruncated));
        }
    }

    private sealed class StubKustoGraphIngestionService : IKustoGraphIngestionService
    {
        public StubKustoGraphIngestionService()
        {
            KustoResultTable resultTable = new(
                "Graph edges",
                [
                    new KustoResultColumn("_SId", "long"),
                    new KustoResultColumn("_TId", "long"),
                    new KustoResultColumn("relationship", "string"),
                ],
                [new KustoResultRow(["1", "2", "AuthenticatedTo"])]);
            Result = new KustoGraphIngestionResult(
                new KustoGraphExportSummary(2, 1, TimeSpan.FromMilliseconds(25), resultTable),
                new GraphImportResult(Guid.NewGuid(), Guid.NewGuid(), 3, 2, 2, 1, 1));
        }

        public KustoGraphIngestionRequest? Request { get; private set; }

        public GraphEntityIdentityConflict? IdentityConflict { get; init; }

        public GraphIdentityResolutionDecision? IdentityResolutionDecision { get; private set; }

        public KustoGraphIngestionResult Result { get; init; }

        public async Task<KustoGraphIngestionResult> ExecuteAsync(
            KustoGraphIngestionRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Request = request;
            if (IdentityConflict is not null)
            {
                GraphIdentityConflictResolver resolver = request.IdentityConflictResolver
                    ?? throw new InvalidOperationException("The manual graph request did not include an identity resolver.");
                IdentityResolutionDecision = await resolver(IdentityConflict, cancellationToken);
                if (IdentityResolutionDecision == GraphIdentityResolutionDecision.Cancel)
                {
                    throw new GraphIdentityResolutionCanceledException();
                }
            }

            return Result;
        }
    }

    private sealed class StubKustoCatalogService : IKustoCatalogService
    {
        public StubKustoCatalogService()
        {
            Databases = [new KustoDatabaseInfo("Samples", "Samples")];
            Schema = CreateSchema("help.kusto.windows.net", "Samples", "StormEvents");
        }

        public IReadOnlyList<KustoDatabaseInfo> Databases { get; init; }

        public KustoDatabaseSchema Schema { get; init; }

        public Task<IReadOnlyList<KustoDatabaseInfo>> GetDatabasesAsync(
            Uri clusterUri,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Databases);
        }

        public Task<KustoDatabaseSchema> GetDatabaseSchemaAsync(
            Uri clusterUri,
            string databaseName,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Schema);
        }
    }

    private sealed class StubKustoConnectionStore : IKustoConnectionStore
    {
        public StubKustoConnectionStore()
        {
            Catalog = new KustoConnectionCatalog([]);
        }

        public KustoConnectionCatalog Catalog { get; init; }

        public KustoConnectionCatalog? SavedCatalog { get; private set; }

        public KustoConnectionCatalog Load()
        {
            return Catalog;
        }

        public void Save(KustoConnectionCatalog catalog)
        {
            SavedCatalog = catalog;
        }
    }

    private sealed class StubKustoCopilotService : IKustoCopilotService
    {
        public StubKustoCopilotService()
        {
            Reply = new KustoCopilotReply("No query changes proposed.", null);
        }

        public KustoCopilotContext? Context { get; private set; }

        public KustoAIProviderKind ProviderKind { get; set; } = KustoAIProviderKind.GitHubCopilot;

        public string ProviderDisplayName { get; set; } = "Test Copilot";

        public bool SupportsInteractiveSignIn => ProviderKind == KustoAIProviderKind.GitHubCopilot;

        public bool SupportsMcp => ProviderKind == KustoAIProviderKind.GitHubCopilot;

        public KustoCopilotOptions? Options { get; private set; }

        public List<string> Requests { get; } = [];

        public TaskCompletionSource<KustoCopilotReply>? PendingReply { get; init; }

        public Exception? ModelsException { get; init; }

        public TaskCompletionSource<IReadOnlyList<KustoCopilotModel>>? PendingModels { get; init; }

        public Exception? SendException { get; init; }

        public Exception? SignInException { get; init; }

        public IReadOnlyList<KustoCopilotModel> AvailableModels { get; set; } =
        [
            new KustoCopilotModel("gpt-test", "Test model"),
            new KustoCopilotModel("gpt-other", "Other model"),
        ];

        public Queue<KustoCopilotReply>? Replies { get; init; }

        public KustoCopilotReply Reply { get; set; }

        public Task SignInAsync(CancellationToken cancellationToken = default)
        {
            return SignInException is null
                ? Task.CompletedTask
                : Task.FromException(SignInException);
        }

        public Task<IReadOnlyList<KustoCopilotModel>> GetModelsAsync(
            CancellationToken cancellationToken = default)
        {
            if (ModelsException is not null)
            {
                return Task.FromException<IReadOnlyList<KustoCopilotModel>>(ModelsException);
            }

            if (PendingModels is not null)
            {
                return PendingModels.Task.WaitAsync(cancellationToken);
            }

            return Task.FromResult(AvailableModels);
        }

        public Task<KustoCopilotReply> SendAsync(
            KustoCopilotContext context,
            string request,
            KustoCopilotOptions options,
            CancellationToken cancellationToken = default)
        {
            Context = context;
            Options = options;
            Requests.Add(request);
            Task<KustoCopilotReply> replyTask;

            if (SendException is not null)
            {
                replyTask = Task.FromException<KustoCopilotReply>(SendException);
            }
            else if (PendingReply is not null)
            {
                replyTask = PendingReply.Task.WaitAsync(cancellationToken);
            }
            else if (Replies?.TryDequeue(out KustoCopilotReply? queuedReply) == true)
            {
                replyTask = Task.FromResult(queuedReply);
            }
            else
            {
                replyTask = Task.FromResult(Reply);
            }

            return replyTask;
        }

        public Task ResetConversationAsync(
            Guid documentId,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class StubKustoExplorerImportService : IKustoExplorerImportService
    {
        public StubKustoExplorerImportService()
        {
            Result = new KustoExplorerImportResult(false, [], 0);
        }

        public KustoExplorerImportResult Result { get; init; }

        public Task<KustoExplorerImportResult> ImportConnectionsAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result);
        }
    }

    private sealed class StubKustoDocumentStore : IKustoDocumentStore
    {
        public StubKustoDocumentStore()
        {
            Workspace = new KustoDocumentWorkspace([], null);
        }

        public KustoDocumentWorkspace Workspace { get; init; }

        public KustoDocumentWorkspace? SavedWorkspace { get; private set; }

        public Exception? SaveException { get; set; }

        public TaskCompletionSource SaveAttempted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public KustoDocumentWorkspace Load()
        {
            return Workspace;
        }

        public void Save(KustoDocumentWorkspace workspace)
        {
            SaveAttempted.TrySetResult();
            if (SaveException is not null)
            {
                throw SaveException;
            }

            SavedWorkspace = workspace;
        }
    }

    private sealed class StubKustoDashboardStore : IKustoDashboardStore
    {
        public KustoDashboardCatalog Catalog { get; init; } = new([]);

        public KustoDashboardCatalog? SavedCatalog { get; private set; }

        public KustoDashboardCatalog Load()
        {
            return Catalog;
        }

        public void Save(KustoDashboardCatalog catalog)
        {
            SavedCatalog = catalog;
        }

        public KustoDashboard Import(Stream stream)
        {
            throw new NotSupportedException();
        }

        public void Export(Stream stream, KustoDashboard dashboard)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubKustoAutomationStore : IKustoAutomationStore
    {
        public StubKustoAutomationStore()
        {
            Catalog = new KustoAutomationCatalog([]);
        }

        public KustoAutomationCatalog Catalog { get; init; }

        public KustoAutomationCatalog? SavedCatalog { get; private set; }

        public KustoAutomationCatalog Load()
        {
            return Catalog;
        }

        public void Save(KustoAutomationCatalog catalog)
        {
            SavedCatalog = catalog;
        }
    }
}
