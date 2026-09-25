using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Sessions;
using OpenKustoExplorer.Domain.Schema;
using OpenKustoExplorer.Infrastructure.Sessions;
using OpenKustoExplorer.Portable.Sessions;
using OpenKustoExplorer.Presentation.Workbench;

namespace OpenKustoExplorer.Presentation.Tests.Workbench;

/// <summary>
/// Verifies recorded-session lifecycle and result annotation behavior.
/// </summary>
public sealed class KustoRecordingWorkspaceViewModelTests
{
    /// <summary>
    /// Verifies session summaries format logical stored bytes for the catalog.
    /// </summary>
    /// <param name="storedBytes">The logical byte count.</param>
    /// <param name="expected">The expected catalog text.</param>
    [Theory]
    [InlineData(0, "0 B stored")]
    [InlineData(1536, "1.5 KB stored")]
    [InlineData(1572864, "1.5 MB stored")]
    public void SessionSummaryFormatsStoredSize(long storedBytes, string expected)
    {
        DateTimeOffset timestamp = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
        KustoRecordedSessionSummary summary = new(
            Guid.NewGuid(),
            "Synthetic session",
            timestamp,
            timestamp,
            2,
            storedBytes);

        KustoRecordedSessionSummaryViewModel viewModel = new(summary);

        Assert.Equal(expected, viewModel.StoredSizeText);
        Assert.Contains(expected, viewModel.AutomationName, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies recorded result pages materialize at most 50 rows across result tables.
    /// </summary>
    [Fact]
    public void RecordedResultsPageFiftyRowsAcrossTables()
    {
        KustoResultTable firstTable = CreatePagedResultTable("First", 0, 40);
        KustoResultTable secondTable = CreatePagedResultTable("Second", 40, 40);
        KustoResultTable thirdTable = CreatePagedResultTable("Third", 80, 30);
        KustoRecordedExecution execution = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            Guid.NewGuid(),
            "Paged query",
            new Uri("https://mock.kusto.example/"),
            "SyntheticSecurity",
            "union First, Second, Third",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            KustoRecordedExecutionStatus.Succeeded,
            null,
            new KustoQueryResult([firstTable, secondTable, thirdTable], TimeSpan.Zero),
            null);
        KustoRecordedExecutionViewModel viewModel = new(execution, [], [], []);

        Assert.Equal(50, viewModel.Tables.Sum(table => table.Rows.Count));
        Assert.Equal("Rows 1-50 of 110", viewModel.PageText);
        Assert.Equal([0, 39], [viewModel.Tables[0].Rows[0].RowIndex, viewModel.Tables[0].Rows[^1].RowIndex]);
        Assert.Equal([0, 9], [viewModel.Tables[1].Rows[0].RowIndex, viewModel.Tables[1].Rows[^1].RowIndex]);
        Assert.False(viewModel.PreviousPageCommand.CanExecute(null));
        Assert.True(viewModel.NextPageCommand.CanExecute(null));
        int pageChanges = 0;
        viewModel.PageChanged += (_, _) => pageChanges++;

        viewModel.NextPageCommand.Execute(null);

        Assert.Equal(50, viewModel.Tables.Sum(table => table.Rows.Count));
        Assert.Equal("Rows 51-100 of 110", viewModel.PageText);
        Assert.Equal([10, 39], [viewModel.Tables[0].Rows[0].RowIndex, viewModel.Tables[0].Rows[^1].RowIndex]);
        Assert.Equal([0, 19], [viewModel.Tables[1].Rows[0].RowIndex, viewModel.Tables[1].Rows[^1].RowIndex]);

        viewModel.NextPageCommand.Execute(null);

        KustoRecordedResultTableViewModel lastPage = Assert.Single(viewModel.Tables);
        Assert.Equal(10, lastPage.Rows.Count);
        Assert.Equal([20, 29], [lastPage.Rows[0].RowIndex, lastPage.Rows[^1].RowIndex]);
        Assert.Equal("Rows 101-110 of 110", viewModel.PageText);
        Assert.Equal(2, pageChanges);
        Assert.True(viewModel.PreviousPageCommand.CanExecute(null));
        Assert.False(viewModel.NextPageCommand.CanExecute(null));
    }

    /// <summary>
    /// Verifies timeline flows describe pertinent inputs and outputs and omit empty executions.
    /// </summary>
    [Fact]
    public void TimelineDescribesColumnFlowAndOmitsZeroRowQueries()
    {
        Guid sessionId = Guid.NewGuid();
        Guid periodId = Guid.NewGuid();
        Guid discoveryExecutionId = Guid.NewGuid();
        Guid emptyExecutionId = Guid.NewGuid();
        DateTimeOffset startedAtUtc = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
        KustoResultTable discoveryTable = new(
            "PrimaryResult",
            [
                new KustoResultColumn("SourceIp", "string"),
                new KustoResultColumn("Fingerprint", "string"),
            ],
            [new KustoResultRow(["192.0.2.10", "SYNTHETIC-FINGERPRINT"])]);
        KustoResultTable emptyTable = new(
            "PrimaryResult",
            discoveryTable.Columns,
            []);
        KustoRecordedRelationDescriptor relation = new(
            "SyntheticEvents",
            true,
            [
                new KustoSourceColumnLineage("SourceIp", "SourceIp"),
                new KustoSourceColumnLineage("Fingerprint", "Fingerprint"),
            ]);
        KustoRecordedExecution discoveryExecution = new(
            discoveryExecutionId,
            sessionId,
            periodId,
            1,
            Guid.NewGuid(),
            "Query 1",
            new Uri("https://mock.kusto.example/"),
            "SyntheticSecurity",
            "SyntheticEvents | where SourceIp == '192.0.2.10'",
            startedAtUtc,
            startedAtUtc.AddSeconds(1),
            KustoRecordedExecutionStatus.Succeeded,
            null,
            new KustoQueryResult([discoveryTable], TimeSpan.FromSeconds(1)),
            relation);
        KustoRecordedExecution emptyExecution = new(
            emptyExecutionId,
            sessionId,
            periodId,
            2,
            Guid.NewGuid(),
            "Query 2",
            new Uri("https://mock.kusto.example/"),
            "SyntheticSecurity",
            "SyntheticEvents | where SourceIp == '192.0.2.20'",
            startedAtUtc.AddMinutes(1),
            startedAtUtc.AddMinutes(1).AddSeconds(1),
            KustoRecordedExecutionStatus.Succeeded,
            null,
            new KustoQueryResult([emptyTable], TimeSpan.FromSeconds(1)),
            relation);
        KustoRecordedInterest input = new(
            Guid.NewGuid(),
            sessionId,
            discoveryExecutionId,
            KustoRecordedInterestSource.QueryPredicate,
            "SourceIp",
            new KustoRecordedValueIdentity("string", "192.0.2.10", false),
            null,
            36,
            12,
            false);
        KustoRecordedInterest output = new(
            Guid.NewGuid(),
            sessionId,
            discoveryExecutionId,
            KustoRecordedInterestSource.ManualCell,
            "Fingerprint",
            new KustoRecordedValueIdentity("string", "SYNTHETIC-FINGERPRINT", false),
            new KustoRecordedValueCoordinate(discoveryExecutionId, 0, 0, 1),
            null,
            null,
            false);
        KustoRecordedInterest emptyInput = new(
            Guid.NewGuid(),
            sessionId,
            emptyExecutionId,
            KustoRecordedInterestSource.QueryPredicate,
            "SourceIp",
            new KustoRecordedValueIdentity("string", "192.0.2.20", false),
            null,
            36,
            12,
            false);
        KustoRecordedSession session = new(
            new KustoRecordedSessionSummary(
                sessionId,
                "Synthetic timeline",
                startedAtUtc,
                startedAtUtc.AddMinutes(2),
                2,
                1024),
            [],
            [discoveryExecution, emptyExecution],
            [input, output, emptyInput],
            [],
            []);

        KustoRecordedSessionViewModel viewModel = new(session);

        Assert.Equal("SyntheticEvents · SourceIp → Fingerprint", viewModel.Executions[0].QueryTitle);
        Assert.True(viewModel.HasTimelineValues);
        Assert.Equal(2, viewModel.TimelineValues.Count);
        Assert.All(viewModel.TimelineValues, value =>
        {
            Assert.Equal(discoveryExecutionId, value.ExecutionId);
            Assert.Equal("SourceIp → Fingerprint", value.FlowText);
        });
        Assert.DoesNotContain(viewModel.TimelineValues, value => value.ExecutionId == emptyExecutionId);
    }

    /// <summary>
    /// Verifies marking a cell on a later page restores that page and global row after reload.
    /// </summary>
    /// <returns>A task that completes after the page-two mark is persisted and reloaded.</returns>
    [Fact]
    public async Task MarkingLaterRecordedResultPageRestoresSelectedRow()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "recorded-sessions.db");

        try
        {
            using SqliteKustoRecordedSessionStore store = new(filePath);
            KustoRecordingWorkspaceViewModel viewModel = CreateViewModel(store);
            await viewModel.OpenRecordingCommand.ExecuteAsync(null);
            viewModel.NewSessionName = "Paged marks";
            await viewModel.StartRecordingCommand.ExecuteAsync(null);
            Guid executionId = Assert.IsType<Guid>(await viewModel.BeginExecutionAsync(
                Guid.NewGuid(),
                "Paged query",
                new KustoQueryRequest(
                    new Uri("https://mock.kusto.example/"),
                    "SyntheticSecurity",
                    "range Value from 0 to 119 step 1"),
                CreateSchema(),
                DateTimeOffset.UtcNow));
            await viewModel.CompleteExecutionAsync(
                executionId,
                new KustoRecordedExecutionCompletion(
                    KustoRecordedExecutionStatus.Succeeded,
                    DateTimeOffset.UtcNow,
                    new KustoQueryResult(
                        [CreatePagedResultTable("PrimaryResult", 0, 120)],
                        TimeSpan.Zero),
                    null));
            await viewModel.StopRecordingCommand.ExecuteAsync(null);
            KustoRecordedExecutionViewModel execution = Assert.IsType<KustoRecordedExecutionViewModel>(
                viewModel.SelectedExecution);
            execution.NextPageCommand.Execute(null);
            KustoResultCellViewModel selectedCell = Assert.Single(execution.Tables).Rows[10].Cells[0];
            Assert.Equal(60, selectedCell.Row.RowIndex);
            viewModel.SetResultContext(selectedCell);

            await viewModel.MarkSelectedCellCommand.ExecuteAsync(null);

            Assert.Equal("Rows 51-100 of 120", viewModel.SelectedExecution.PageText);
            Assert.Equal(50, Assert.Single(viewModel.SelectedExecution.Tables).Rows.Count);
            Assert.Contains("row 61", viewModel.SelectedResultValueLocationText, StringComparison.Ordinal);
            Assert.True(viewModel.SelectedResultValueIsMarked);
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies every value in a live result column is marked in one idempotent operation.
    /// </summary>
    /// <returns>A task that completes after the column marks are persisted.</returns>
    [Fact]
    public async Task MarkLiveColumnPersistsEveryValue()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "recorded-sessions.db");

        try
        {
            using SqliteKustoRecordedSessionStore store = new(filePath);
            KustoRecordingWorkspaceViewModel viewModel = CreateViewModel(store);
            await viewModel.OpenRecordingCommand.ExecuteAsync(null);
            viewModel.NewSessionName = "Column marks";
            await viewModel.StartRecordingCommand.ExecuteAsync(null);
            Guid executionId = Assert.IsType<Guid>(await viewModel.BeginExecutionAsync(
                Guid.NewGuid(),
                "Source addresses",
                new KustoQueryRequest(
                    new Uri("https://mock.kusto.example/"),
                    "SyntheticSecurity",
                    "OutboundBrowsing | take 2"),
                CreateSchema(),
                DateTimeOffset.UtcNow));
            KustoResultTable table = CreateResultTable();
            await viewModel.CompleteExecutionAsync(
                executionId,
                new KustoRecordedExecutionCompletion(
                    KustoRecordedExecutionStatus.Succeeded,
                    DateTimeOffset.UtcNow,
                    new KustoQueryResult([table], TimeSpan.FromMilliseconds(5)),
                    null));
            KustoResultRowViewModel[] rows = table.Rows
                .Select((row, rowIndex) => new KustoResultRowViewModel(row, rowIndex, table.Columns))
                .ToArray();

            await viewModel.MarkLiveColumnAsync(executionId, rows, 1);
            await viewModel.MarkLiveColumnAsync(executionId, rows, 1);

            Assert.All(rows, row => Assert.True(row.Cells[1].IsRecordedPertinent));
            KustoRecordedSession session = Assert.IsType<KustoRecordedSession>(
                await store.GetSessionAsync(viewModel.SelectedSessionSummary!.Id));
            Assert.Equal(2, session.Marks.Count(mark => mark.Kind == KustoRecordedMarkKind.Cell));
            Assert.Equal(
                2,
                session.Interests.Count(interest =>
                    interest.Source == KustoRecordedInterestSource.ManualCell));
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies new recording, automatic predicate interest, live marking, stop, and inspection.
    /// </summary>
    /// <returns>A task that completes after the workspace workflow.</returns>
    [Fact]
    public async Task RecordingWorkflowPersistsAutomaticAndManualInterests()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "recorded-sessions.db");

        try
        {
            using SqliteKustoRecordedSessionStore store = new(filePath);
            KustoRecordingWorkspaceViewModel viewModel = CreateViewModel(store);
            await viewModel.OpenRecordingCommand.ExecuteAsync(null);
            viewModel.NewSessionName = "Security research";

            await viewModel.StartRecordingCommand.ExecuteAsync(null);

            Assert.True(viewModel.IsRecording);
            Assert.Equal("Security research", viewModel.ActiveSessionName);
            const string Query = "OutboundBrowsing | where url == \"https://malware.example.test/c2\"";
            Guid? executionId = await viewModel.BeginExecutionAsync(
                Guid.NewGuid(),
                "Query 1",
                new KustoQueryRequest(new Uri("https://mock.kusto.example/"), "SyntheticSecurity", Query),
                CreateSchema(),
                DateTimeOffset.UtcNow);
            Assert.NotNull(executionId);
            KustoResultTable table = CreateResultTable();
            await viewModel.CompleteExecutionAsync(
                executionId,
                new KustoRecordedExecutionCompletion(
                    KustoRecordedExecutionStatus.Succeeded,
                    DateTimeOffset.UtcNow,
                    new KustoQueryResult([table], TimeSpan.FromMilliseconds(5)),
                    null));
            KustoResultRowViewModel row = new(
                table.Rows[0],
                0,
                table.Columns);

            Assert.True(viewModel.IsInteresting(row.Cells[0].TypeName, row.Cells[0].Value));
            await viewModel.MarkLiveCellAsync(executionId.Value, row.Cells[1]);
            Assert.True(row.Cells[1].IsRecordedPertinent);
            Assert.NotEqual("#00000000", row.Cells[1].RecordingAccentHex);
            Assert.NotEqual("#00000000", row.Cells[1].RecordingHighlightHex);
            Assert.True(viewModel.IsInteresting(row.Cells[1].TypeName, row.Cells[1].Value));
            Assert.True(viewModel.IsManualInterestMatch(CreateValue("source=192.0.2.56; action=allowed")));
            await viewModel.UnmarkLiveCellAsync(executionId.Value, row.Cells[1]);
            Assert.False(row.Cells[1].IsRecordedPertinent);
            Assert.False(viewModel.IsInteresting(row.Cells[1].TypeName, row.Cells[1].Value));
            Assert.False(viewModel.IsManualInterestMatch(CreateValue("source=192.0.2.56; action=allowed")));
            await viewModel.MarkLiveCellAsync(executionId.Value, row.Cells[1]);
            KustoResultRowViewModel matchingRow = new(table.Rows[0], 1, table.Columns);
            KustoResultRowViewModel differentRow = new(
                new KustoResultRow(
                [
                    CreateValue("https://different.example.test/"),
                    CreateValue("192.0.2.56"),
                ]),
                2,
                table.Columns);
            Assert.NotEqual(matchingRow.Cells[0].Text, differentRow.Cells[0].Text);

            await viewModel.StopRecordingCommand.ExecuteAsync(null);

            Assert.False(viewModel.IsRecording);
            KustoRecordedSessionSummaryViewModel summary = Assert.Single(viewModel.Sessions);
            await viewModel.SelectSessionCommand.ExecuteAsync(summary);
            Assert.NotNull(viewModel.SelectedSession);
            Assert.Equal(3, viewModel.SelectedSession.Session.Interests.Count);
            Assert.Equal(2, viewModel.SelectedSession.Session.Marks.Count);
            Assert.Equal(2, viewModel.SelectedSession.TimelineValues.Count);
            Assert.Equal("2 discoveries across 1 query", viewModel.SelectedSession.TimelineCountText);
            Assert.Equal(
                2,
                viewModel.SelectedSession.PertinentValues
                    .Select(value => value.AccentColorHex)
                    .Distinct(StringComparer.Ordinal)
                    .Count());
            Assert.All(viewModel.SelectedSession.TimelineValues, timelineValue =>
                Assert.Equal(
                    viewModel.SelectedSession.PertinentValues.Single(
                        value => value.ValueText == timelineValue.ValueText).AccentColorHex,
                    timelineValue.AccentColorHex));
            Assert.Collection(
                viewModel.SelectedSession.PertinentValues,
                value =>
                {
                    Assert.Equal("192.0.2.56", value.ValueText);
                    Assert.Equal("Added by you", value.SourceText);
                    Assert.True(value.IsAddedByUser);
                },
                value =>
                {
                    Assert.Equal("https://malware.example.test/c2", value.ValueText);
                    Assert.Equal("Extracted", value.SourceText);
                    Assert.True(value.IsExtracted);
                });
            KustoRecordedPertinentValueViewModel startValue = viewModel.SelectedSession.PertinentValues.Single(
                value => value.ValueText == "https://malware.example.test/c2");
            KustoRecordedPertinentValueViewModel endValue = viewModel.SelectedSession.PertinentValues.Single(
                value => value.ValueText == "192.0.2.56");

            await viewModel.SetEndpointFromPertinentValueAsync(
                startValue,
                KustoChainEndpointRole.Start);
            await viewModel.SetEndpointFromPertinentValueAsync(
                endValue,
                KustoChainEndpointRole.End);

            Assert.Equal("https://malware.example.test/c2", viewModel.ChainStartValueText);
            Assert.Equal("192.0.2.56", viewModel.ChainEndValueText);
            Assert.True(viewModel.CanGenerateChain);
            Assert.Single(viewModel.SelectedSession.TimelineValues, value => value.IsChainStart);
            Assert.Single(viewModel.SelectedSession.TimelineValues, value => value.IsChainEnd);
            KustoResultExportFile export = viewModel.CreatePertinentValuesCsvExport();
            string csv = Encoding.UTF8.GetString(export.Content);
            Assert.EndsWith("-pertinent-values.csv", export.SuggestedFileName, StringComparison.Ordinal);
            Assert.Equal(
                ["192.0.2.56", "https://malware.example.test/c2"],
                csv.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries));

            const string FailureMessage =
                "The inferred chain contains weak or unsupported pivots and cannot be generated safely";
            viewModel.ReportChainGenerationUnavailable(FailureMessage);
            Assert.True(viewModel.IsChainGenerationDialogOpen);
            Assert.Equal(FailureMessage, viewModel.ChainGenerationDialogMessage);
            Assert.Contains("Append direct queries", viewModel.ChainGenerationGuidanceText, StringComparison.Ordinal);
            viewModel.DismissChainGenerationDialogCommand.Execute(null);
            Assert.False(viewModel.IsChainGenerationDialogOpen);

            viewModel.ReportChainGenerationUnavailable(FailureMessage);
            Assert.True(viewModel.AddChainEvidenceCommand.CanExecute(null));
            viewModel.AddChainEvidenceCommand.Execute(null);
            Assert.False(viewModel.IsChainGenerationDialogOpen);
            Assert.True(viewModel.IsRecordingDialogOpen);
            Assert.True(viewModel.IsAppendMode);
            Assert.Equal(summary.Id, viewModel.SelectedAppendSession!.Id);
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies pause discards in-flight capture, blocks new capture, and resumes the same named session.
    /// </summary>
    /// <returns>A task that completes after resumed capture is persisted.</returns>
    [Fact]
    public async Task PauseDiscardsInFlightExecutionAndResumeStartsNewPeriod()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Join(directoryPath, "recorded-sessions.db");

        try
        {
            using SqliteKustoRecordedSessionStore store = new(filePath);
            KustoRecordingWorkspaceViewModel viewModel = CreateViewModel(store);
            await viewModel.OpenRecordingCommand.ExecuteAsync(null);
            viewModel.NewSessionName = "Paused investigation";
            await viewModel.StartRecordingCommand.ExecuteAsync(null);
            Guid discardedExecutionId = Assert.IsType<Guid>(await viewModel.BeginExecutionAsync(
                Guid.NewGuid(),
                "Discarded query",
                new KustoQueryRequest(
                    new Uri("https://mock.kusto.example/"),
                    "SyntheticSecurity",
                    "OutboundBrowsing | take 1"),
                CreateSchema(),
                DateTimeOffset.UtcNow));

            await viewModel.PauseRecordingCommand.ExecuteAsync(null);

            Assert.False(viewModel.IsRecording);
            Assert.True(viewModel.IsPaused);
            Assert.True(viewModel.HasActiveRecording);
            Assert.False(await viewModel.CompleteExecutionAsync(
                discardedExecutionId,
                new KustoRecordedExecutionCompletion(
                    KustoRecordedExecutionStatus.Succeeded,
                    DateTimeOffset.UtcNow,
                    new KustoQueryResult([CreateResultTable()], TimeSpan.Zero),
                    null)));
            Assert.Null(await viewModel.BeginExecutionAsync(
                Guid.NewGuid(),
                "Paused query",
                new KustoQueryRequest(
                    new Uri("https://mock.kusto.example/"),
                    "SyntheticSecurity",
                    "OutboundBrowsing | take 1"),
                CreateSchema(),
                DateTimeOffset.UtcNow));

            await viewModel.ResumeRecordingCommand.ExecuteAsync(null);

            Assert.True(viewModel.IsRecording);
            Assert.False(viewModel.IsPaused);
            Guid retainedExecutionId = Assert.IsType<Guid>(await viewModel.BeginExecutionAsync(
                Guid.NewGuid(),
                "Retained query",
                new KustoQueryRequest(
                    new Uri("https://mock.kusto.example/"),
                    "SyntheticSecurity",
                    "OutboundBrowsing | take 1"),
                CreateSchema(),
                DateTimeOffset.UtcNow));
            Assert.True(await viewModel.CompleteExecutionAsync(
                retainedExecutionId,
                new KustoRecordedExecutionCompletion(
                    KustoRecordedExecutionStatus.Succeeded,
                    DateTimeOffset.UtcNow,
                    new KustoQueryResult([CreateResultTable()], TimeSpan.Zero),
                    null)));
            await viewModel.StopRecordingCommand.ExecuteAsync(null);

            KustoRecordedSession session = Assert.IsType<KustoRecordedSession>(
                await store.GetSessionAsync(viewModel.SelectedSessionSummary!.Id));
            Assert.Equal(2, session.Periods.Count);
            Assert.Equal(retainedExecutionId, Assert.Single(session.Executions).Id);
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies that setting a chain endpoint does not move the user away from the selected query result.
    /// </summary>
    /// <returns>A task that completes after the endpoint is persisted.</returns>
    [Fact]
    public async Task SettingEndpointPreservesSelectedExecution()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "recorded-sessions.db");

        try
        {
            using SqliteKustoRecordedSessionStore store = new(filePath);
            KustoRecordingWorkspaceViewModel viewModel = CreateViewModel(store);
            await viewModel.OpenRecordingCommand.ExecuteAsync(null);
            viewModel.NewSessionName = "Endpoint selection";
            await viewModel.StartRecordingCommand.ExecuteAsync(null);

            Guid firstExecutionId = Assert.IsType<Guid>(await viewModel.BeginExecutionAsync(
                Guid.NewGuid(),
                "First query",
                new KustoQueryRequest(
                    new Uri("https://mock.kusto.example/"),
                    "SyntheticSecurity",
                    "OutboundBrowsing | where url == \"https://malware.example.test/c2\""),
                CreateSchema(),
                DateTimeOffset.UtcNow));
            await viewModel.CompleteExecutionAsync(
                firstExecutionId,
                new KustoRecordedExecutionCompletion(
                    KustoRecordedExecutionStatus.Succeeded,
                    DateTimeOffset.UtcNow,
                    new KustoQueryResult(
                        CreateLegacyProtocolResultTables(),
                        TimeSpan.FromMilliseconds(5)),
                    null));

            Guid secondExecutionId = Assert.IsType<Guid>(await viewModel.BeginExecutionAsync(
                Guid.NewGuid(),
                "Second query",
                new KustoQueryRequest(
                    new Uri("https://mock.kusto.example/"),
                    "SyntheticSecurity",
                    "OutboundBrowsing | take 1"),
                CreateSchema(),
                DateTimeOffset.UtcNow.AddSeconds(1)));
            await viewModel.CompleteExecutionAsync(
                secondExecutionId,
                new KustoRecordedExecutionCompletion(
                    KustoRecordedExecutionStatus.Succeeded,
                    DateTimeOffset.UtcNow.AddSeconds(1),
                    new KustoQueryResult([CreateResultTable()], TimeSpan.FromMilliseconds(5)),
                    null));
            await viewModel.StopRecordingCommand.ExecuteAsync(null);

            KustoRecordedSessionViewModel session = Assert.IsType<KustoRecordedSessionViewModel>(
                viewModel.SelectedSession);
            Assert.Equal(
                ["OutboundBrowsing · by url", "OutboundBrowsing · url"],
                session.Executions.Select(execution => execution.QueryTitle));
            KustoRecordedExecutionViewModel firstExecution = session.Executions.Single(
                execution => execution.Id == firstExecutionId);
            KustoRecordedExecutionViewModel secondExecution = session.Executions.Single(
                execution => execution.Id == secondExecutionId);
            KustoRecordedTimelineValueViewModel timelineValue = Assert.Single(session.TimelineValues);
            Assert.Equal(firstExecutionId, timelineValue.ExecutionId);
            viewModel.SelectedExecution = secondExecution;

            viewModel.SelectTimelineValue(timelineValue);

            Assert.Equal(firstExecutionId, viewModel.SelectedExecution.Id);
            KustoRecordedResultTableViewModel selectedTable = Assert.Single(firstExecution.Tables);
            Assert.Equal("Results", selectedTable.DisplayName);
            Assert.Contains("url", selectedTable.MetadataText, StringComparison.Ordinal);
            KustoResultCellViewModel startCell = selectedTable.Rows[0].Cells[0];
            Assert.True(viewModel.HasSelectedResultValue);
            Assert.True(startCell.IsActionTarget);
            Assert.Equal(0, viewModel.SelectedTable!.TableOrdinal);

            await viewModel.SetEndpointFromTimelineValueAsync(timelineValue, KustoChainEndpointRole.Start);

            Assert.Equal(firstExecutionId, viewModel.SelectedSession.SelectedExecution!.Id);
            Assert.Equal(0, viewModel.SelectedTable.TableOrdinal);
            Assert.True(viewModel.HasChainStart);
            Assert.False(viewModel.HasChainEnd);
            Assert.False(viewModel.CanGenerateChain);
            Assert.Equal("https://malware.example.test/c2", viewModel.ChainStartValueText);
            Assert.Contains("OutboundBrowsing · by url", viewModel.ChainStartLocationText, StringComparison.Ordinal);
            Assert.True(viewModel.HasSelectedResultValue);

            viewModel.SelectedExecution = secondExecution;
            Assert.False(startCell.IsActionTarget);
            viewModel.SetResultContext(secondExecution.SelectedTable!.Rows[0].Cells[1]);
            await viewModel.SetChainEndCommand.ExecuteAsync(null);

            Assert.True(viewModel.HasChainStart);
            Assert.True(viewModel.HasChainEnd);
            Assert.True(viewModel.CanGenerateChain);
            Assert.Equal("192.0.2.56", viewModel.ChainEndValueText);
            Assert.Contains("OutboundBrowsing · url", viewModel.ChainEndLocationText, StringComparison.Ordinal);
            await viewModel.MarkSelectedColumnCommand.ExecuteAsync(null);
            Assert.All(viewModel.SelectedTable.Rows, row => Assert.True(row.Cells[1].IsRecordedPertinent));
            await viewModel.MarkSelectedCellCommand.ExecuteAsync(null);
            Assert.True(viewModel.SelectedResultValueIsMarked);
            Assert.True(viewModel.SelectedTable.Rows[0].Cells[1].IsRecordedPertinent);
            Assert.True(viewModel.SelectedTable.Rows[1].Cells[0].IsRecordedManualMatch);
            await viewModel.UnmarkSelectedCellCommand.ExecuteAsync(null);
            Assert.False(viewModel.SelectedResultValueIsMarked);
            Assert.False(viewModel.SelectedTable.Rows[1].Cells[0].IsRecordedManualMatch);

            viewModel.OpenDeleteExecutionCommand.Execute(null);
            Assert.True(viewModel.IsDeleteExecutionConfirmationOpen);
            await viewModel.DeleteExecutionCommand.ExecuteAsync(null);

            KustoRecordedExecutionViewModel remainingExecution = Assert.Single(
                viewModel.SelectedSession.Executions);
            Assert.Equal(firstExecutionId, remainingExecution.Id);
            Assert.Equal(firstExecutionId, viewModel.SelectedExecution.Id);
            Assert.True(viewModel.HasChainStart);
            Assert.False(viewModel.HasChainEnd);
            Assert.False(viewModel.CanGenerateChain);
            Assert.False(viewModel.IsDeleteExecutionConfirmationOpen);

            viewModel.OpenRenameExecution(remainingExecution);
            Assert.True(viewModel.IsRenameExecutionOpen);
            viewModel.RenameExecutionName = "Initial URL pivot";
            await viewModel.SaveRenameExecutionCommand.ExecuteAsync(null);

            Assert.False(viewModel.IsRenameExecutionOpen);
            Assert.Equal("Initial URL pivot", viewModel.SelectedExecution.QueryTitle);
            Assert.Contains("Initial URL pivot", viewModel.ChainStartLocationText, StringComparison.Ordinal);
            KustoRecordedSession renamedSession = Assert.IsType<KustoRecordedSession>(
                await store.GetSessionAsync(viewModel.SelectedSession.Id));
            Assert.Equal("Initial URL pivot", Assert.Single(renamedSession.Executions).DisplayName);
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies an incompatible database opens recovery and can be replaced without throwing through the UI command.
    /// </summary>
    /// <returns>A task that completes after recovery creates empty storage.</returns>
    [Fact]
    public async Task RefreshOffersRecoveryForIncompatibleDatabase()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "recorded-sessions.db");

        try
        {
            using (SqliteConnection connection = new($"Data Source={filePath}"))
            {
                await connection.OpenAsync();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "PRAGMA user_version = 4;";
                await command.ExecuteNonQueryAsync();
            }

            using SqliteKustoRecordedSessionStore store = new(filePath);
            KustoRecordingWorkspaceViewModel viewModel = CreateViewModel(store);

            await viewModel.RefreshCommand.ExecuteAsync(null);

            Assert.True(viewModel.IsDatabaseRecoveryOpen);
            Assert.True(viewModel.CanResetDatabase);
            Assert.Equal(directoryPath, viewModel.DatabaseDirectoryPath);
            Assert.Contains("version 4", viewModel.DatabaseRecoveryMessage, StringComparison.Ordinal);

            await viewModel.ResetDatabaseCommand.ExecuteAsync(null);

            Assert.False(viewModel.IsDatabaseRecoveryOpen);
            Assert.False(viewModel.HasDatabaseRecoveryError);
            Assert.Empty(viewModel.Sessions);
            Assert.Empty(await store.GetSessionsAsync());
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies archive export requires a selected finalized session and delegates its stream.
    /// </summary>
    /// <returns>A task that completes after export and recording state changes.</returns>
    [Fact]
    public async Task ArchiveExportTracksSelectionAndRecordingState()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "recorded-sessions.db");

        try
        {
            using SqliteKustoRecordedSessionStore store = new(filePath);
            FakeRecordedSessionArchiveService archiveService = new();
            KustoRecordingWorkspaceViewModel viewModel = CreateViewModel(store, archiveService);

            Assert.True(viewModel.CanImportSession);
            Assert.False(viewModel.CanExportSelectedSession);
            KustoRecordingPeriod period = await store.CreateSessionAsync(
                "Archive/session",
                DateTimeOffset.UtcNow);
            await store.StopRecordingAsync(period.Id, DateTimeOffset.UtcNow.AddMinutes(1));
            await viewModel.RefreshCommand.ExecuteAsync(null);

            Assert.True(viewModel.CanExportSelectedSession);
            Assert.Equal("Archive_session.okesession", viewModel.SuggestedArchiveFileName);
            viewModel.OpenExportSessionCommand.Execute(null);
            Assert.True(viewModel.IsExportConfirmationOpen);
            viewModel.CancelExportSessionCommand.Execute(null);
            Assert.False(viewModel.IsExportConfirmationOpen);
            using MemoryStream destination = new();

            await viewModel.ExportSelectedSessionAsync(destination);

            Assert.Equal(period.SessionId, archiveService.ExportedSessionId);
            Assert.Equal(1, destination.Length);
            Assert.False(viewModel.IsLoading);

            await viewModel.OpenRecordingCommand.ExecuteAsync(null);
            viewModel.NewSessionName = "Active archive session";
            await viewModel.StartRecordingCommand.ExecuteAsync(null);

            Assert.True(viewModel.HasActiveRecording);
            Assert.False(viewModel.CanImportSession);
            Assert.False(viewModel.CanExportSelectedSession);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => viewModel.ExportSelectedSessionAsync(new MemoryStream()));
            await viewModel.StopRecordingCommand.ExecuteAsync(null);
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies importing a session refreshes and selects the returned local copy.
    /// </summary>
    /// <returns>A task that completes after the imported copy is selected.</returns>
    [Fact]
    public async Task ArchiveImportRefreshesAndSelectsImportedCopy()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "recorded-sessions.db");

        try
        {
            using SqliteKustoRecordedSessionStore store = new(filePath);
            FakeRecordedSessionArchiveService archiveService = new();
            archiveService.ImportAction = async (_, cancellationToken) =>
            {
                DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;
                KustoRecordingPeriod period = await store.CreateSessionAsync(
                    "Imported copy",
                    startedAtUtc,
                    cancellationToken);
                await store.StopRecordingAsync(
                    period.Id,
                    startedAtUtc.AddMinutes(1),
                    cancellationToken);
                return Assert.Single(await store.GetSessionsAsync(cancellationToken));
            };
            KustoRecordingWorkspaceViewModel viewModel = CreateViewModel(store, archiveService);
            using MemoryStream source = new([42]);

            KustoRecordedSessionSummary imported = await viewModel.ImportSessionCopyAsync(source);

            Assert.Equal(1, archiveService.ImportCount);
            Assert.Equal("Imported copy", imported.Name);
            Assert.Equal(imported.Id, viewModel.SelectedSessionSummary?.Id);
            Assert.Equal(imported.Id, viewModel.SelectedSession?.Id);
            Assert.False(viewModel.IsLoading);
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies a failed archive import keeps the current selection and clears loading state.
    /// </summary>
    /// <returns>A task that completes after the import failure.</returns>
    [Fact]
    public async Task ArchiveImportFailurePreservesSelection()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "recorded-sessions.db");

        try
        {
            using SqliteKustoRecordedSessionStore store = new(filePath);
            KustoRecordingPeriod period = await store.CreateSessionAsync("Existing", DateTimeOffset.UtcNow);
            await store.StopRecordingAsync(period.Id, DateTimeOffset.UtcNow.AddMinutes(1));
            FakeRecordedSessionArchiveService archiveService = new()
            {
                ImportException = new InvalidDataException("Synthetic invalid archive"),
            };
            KustoRecordingWorkspaceViewModel viewModel = CreateViewModel(store, archiveService);
            await viewModel.RefreshCommand.ExecuteAsync(null);
            Guid selectedSessionId = Assert.IsType<KustoRecordedSessionSummaryViewModel>(
                viewModel.SelectedSessionSummary).Id;

            InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
                () => viewModel.ImportSessionCopyAsync(new MemoryStream([1])));

            Assert.Equal("Synthetic invalid archive", exception.Message);
            Assert.Equal(selectedSessionId, viewModel.SelectedSessionSummary?.Id);
            Assert.Equal(selectedSessionId, viewModel.SelectedSession?.Id);
            Assert.False(viewModel.IsLoading);
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    private static KustoRecordingWorkspaceViewModel CreateViewModel(
        SqliteKustoRecordedSessionStore store,
        IKustoRecordedSessionArchiveService? archiveService = null)
    {
        KustoRecordedChainSearcher searcher = new(store);
        return new KustoRecordingWorkspaceViewModel(
            store,
            new KustoPredicateInterestExtractor(),
            new KustoRecordedRelationExtractor(),
            searcher,
            new KustoRecordedRelationPlanner(),
            new KustoRecordedChainQueryGenerator(),
            archiveService: archiveService);
    }

    private static KustoDatabaseSchema CreateSchema()
    {
        KustoTableSchema table = new(
            "OutboundBrowsing",
            [
                new KustoColumnSchema("url", KustoScalarType.Text),
                new KustoColumnSchema("src_ip", KustoScalarType.Text),
            ]);
        return new KustoDatabaseSchema("mock.kusto.example", "SyntheticSecurity", [table]);
    }

    private static KustoResultTable CreateResultTable(string name = "PrimaryResult")
    {
        return new KustoResultTable(
            name,
            [
                new KustoResultColumn("url", "string"),
                new KustoResultColumn("src_ip", "string"),
            ],
            [
                new KustoResultRow(
                [
                    CreateValue("https://malware.example.test/c2"),
                    CreateValue("192.0.2.56"),
                ]),
                new KustoResultRow(
                [
                    CreateValue("Connection from 192.0.2.56 was observed"),
                    CreateValue("192.0.2.10"),
                ]),
            ]);
    }

    private static KustoResultTable CreatePagedResultTable(string name, int start, int count)
    {
        return new KustoResultTable(
            name,
            [new KustoResultColumn("Value", "long")],
            Enumerable.Range(start, count)
                .Select(value => new KustoResultRow([value.ToString(System.Globalization.CultureInfo.InvariantCulture)])));
    }

    private static IReadOnlyList<KustoResultTable> CreateLegacyProtocolResultTables()
    {
        return
        [
            CreateResultTable("Table_0"),
            new KustoResultTable(
                "Table_1",
                [new KustoResultColumn("Value", "dynamic")],
                [new KustoResultRow(["{}"])]),
            new KustoResultTable(
                "Table_2",
                [
                    new KustoResultColumn("Severity", "int"),
                    new KustoResultColumn("StatusDescription", "string"),
                ],
                [new KustoResultRow(["4", "Query completed"])]),
            new KustoResultTable(
                "Table_3",
                [
                    new KustoResultColumn("Ordinal", "long"),
                    new KustoResultColumn("Kind", "string"),
                    new KustoResultColumn("Name", "string"),
                    new KustoResultColumn("Id", "guid"),
                    new KustoResultColumn("PrettyName", "string"),
                ],
                [
                    new KustoResultRow(["0", "QueryResult", "PrimaryResult", Guid.NewGuid().ToString(), string.Empty]),
                    new KustoResultRow(["1", "QueryProperties", "@ExtendedProperties", Guid.NewGuid().ToString(), string.Empty]),
                    new KustoResultRow(["2", "QueryStatus", "QueryStatus", Guid.Empty.ToString(), string.Empty]),
                ]),
        ];
    }

    private static KustoResultValue CreateValue(string value)
    {
        return new KustoResultValue(value, JsonSerializer.Serialize(value), false);
    }

    private static string CreateTemporaryDirectory()
    {
        string directoryPath = Path.Combine(
            Path.GetTempPath(),
            $"OpenKustoExplorer-RecordingViewModel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        return directoryPath;
    }

    private static void DeleteTemporaryDirectory(string directoryPath)
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directoryPath))
        {
            Directory.Delete(directoryPath, true);
        }
    }

    private sealed class FakeRecordedSessionArchiveService : IKustoRecordedSessionArchiveService
    {
        public Func<Stream, CancellationToken, Task<KustoRecordedSessionSummary>>? ImportAction { get; set; }

        public Exception? ImportException { get; set; }

        public Guid? ExportedSessionId { get; private set; }

        public int ImportCount { get; private set; }

        public Task ExportAsync(
            Guid sessionId,
            Stream destination,
            CancellationToken cancellationToken = default)
        {
            ExportedSessionId = sessionId;
            destination.WriteByte(42);
            return Task.CompletedTask;
        }

        public Task<KustoRecordedSessionSummary> ImportCopyAsync(
            Stream source,
            CancellationToken cancellationToken = default)
        {
            ImportCount++;
            if (ImportException is not null)
            {
                return Task.FromException<KustoRecordedSessionSummary>(ImportException);
            }

            return ImportAction?.Invoke(source, cancellationToken)
                ?? Task.FromException<KustoRecordedSessionSummary>(
                    new InvalidOperationException("No synthetic archive import was configured."));
        }
    }
}
