using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;
using OpenKustoExplorer.Application.Assistance;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Application.Sessions;
using OpenKustoExplorer.Domain.Schema;
using OpenKustoExplorer.Infrastructure.Assistance;
using OpenKustoExplorer.Infrastructure.Sessions;

namespace OpenKustoExplorer.Infrastructure.Tests.Assistance;

/// <summary>
/// Verifies bounded and session-pinned Recorded Sessions Copilot tools.
/// </summary>
public sealed class CopilotRecordedSessionToolsTests
{
    /// <summary>
    /// Verifies tools expose compact metadata and bounded rows only from the pinned session.
    /// </summary>
    /// <returns>A task that completes after the tool operations are verified.</returns>
    [Fact]
    public async Task ToolsAreBoundedAndPinnedToSelectedSession()
    {
        string directoryPath = Path.Combine(
            Path.GetTempPath(),
            $"OpenKustoExplorer-CopilotSessions-{Guid.NewGuid():N}");
        string filePath = Path.Combine(directoryPath, "recorded-sessions.db");
        Directory.CreateDirectory(directoryPath);

        try
        {
            using SqliteKustoRecordedSessionStore store = new(filePath);
            DateTimeOffset startedAtUtc = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
            KustoRecordingPeriod selectedPeriod = await store.CreateSessionAsync(
                "Synthetic Copilot investigation",
                startedAtUtc);
            Guid selectedExecutionId = await RecordAsync(
                store,
                selectedPeriod,
                "SyntheticEvents | project Indicator, Detail",
                Enumerable.Range(0, 75)
                    .Select(index => new KustoResultRow(
                    [
                        $"match-{index:D3}",
                        index == 0 ? new string('x', 700) : $"detail-{index:D3}",
                    ]))
                    .ToArray(),
                startedAtUtc);
            KustoRecordingPeriod otherPeriod = await store.CreateSessionAsync(
                "Other synthetic session",
                startedAtUtc.AddHours(1));
            Guid otherExecutionId = await RecordAsync(
                store,
                otherPeriod,
                "OtherEvents | take 1",
                [new KustoResultRow(["outside-session", "not-visible"])],
                startedAtUtc.AddHours(1));
            KustoDatabaseSchema schema = new(
                "mock.kusto.example",
                "SyntheticSecurity",
                [
                    new KustoTableSchema(
                        "SyntheticEvents",
                        [
                            new KustoColumnSchema("Indicator", KustoScalarType.Text),
                            new KustoColumnSchema("Detail", KustoScalarType.Text),
                        ]),
                ]);
            KustoCopilotRecordedSessionScope scope = new(selectedPeriod.SessionId, schema);
            KustoRecordedChainSearcher searcher = new(store);
            KustoRecordedRelationPlanner planner = new();
            KustoRecordedChainQueryGenerator generator = new();

            IReadOnlyList<AIFunction> tools = CopilotRecordedSessionTools.Create(
                store,
                searcher,
                planner,
                generator,
                scope);
            string overview = await CopilotRecordedSessionTools.GetOverviewAsync(
                store,
                selectedPeriod.SessionId,
                CancellationToken.None);
            string query = await CopilotRecordedSessionTools.GetQueryAsync(
                store,
                selectedPeriod.SessionId,
                selectedExecutionId.ToString("D"),
                CancellationToken.None);
            string crossSessionQuery = await CopilotRecordedSessionTools.GetQueryAsync(
                store,
                selectedPeriod.SessionId,
                otherExecutionId.ToString("D"),
                CancellationToken.None);
            string search = await CopilotRecordedSessionTools.SearchResultsAsync(
                store,
                selectedPeriod.SessionId,
                "match-",
                string.Empty,
                500,
                CancellationToken.None);
            string page = await CopilotRecordedSessionTools.GetResultPageAsync(
                store,
                selectedPeriod.SessionId,
                selectedExecutionId.ToString("D"),
                0,
                0,
                500,
                CancellationToken.None);

            Assert.Equal(5, tools.Count);
            Assert.Contains(tools, tool => tool.Name == CopilotRecordedSessionTools.GetOverviewToolName);
            Assert.Contains(tools, tool => tool.Name == CopilotRecordedSessionTools.SearchResultsToolName);
            Assert.Contains(tools, tool => tool.Name == CopilotRecordedSessionTools.GenerateChainQueryToolName);
            Assert.DoesNotContain("match-000", overview, StringComparison.Ordinal);
            Assert.Contains("\"retainedRowCount\":75", overview, StringComparison.Ordinal);
            Assert.Contains("SyntheticEvents | project Indicator, Detail", query, StringComparison.Ordinal);
            Assert.Contains("\"error\"", crossSessionQuery, StringComparison.Ordinal);
            Assert.DoesNotContain("outside-session", search, StringComparison.Ordinal);
            Assert.InRange(Encoding.UTF8.GetByteCount(search), 1, 64 * 1024);
            Assert.InRange(Encoding.UTF8.GetByteCount(page), 1, 64 * 1024);

            using JsonDocument searchDocument = JsonDocument.Parse(search);
            JsonElement searchRoot = searchDocument.RootElement;
            Assert.True(searchRoot.GetProperty("matchesTruncated").GetBoolean());
            Assert.Equal(50, searchRoot.GetProperty("matches").GetArrayLength());
            Assert.Equal(
                $"{selectedExecutionId:D}/0/0/0",
                searchRoot.GetProperty("matches")[0]
                    .GetProperty("row")
                    .GetProperty("cells")[0]
                    .GetProperty("coordinate")
                    .GetString());

            using JsonDocument pageDocument = JsonDocument.Parse(page);
            JsonElement pageRoot = pageDocument.RootElement;
            Assert.Equal(50, pageRoot.GetProperty("rows").GetArrayLength());
            Assert.Equal(50, pageRoot.GetProperty("nextStartRow").GetInt32());
            string longCellValue = pageRoot.GetProperty("rows")[0]
                .GetProperty("cells")[1]
                .GetProperty("value")
                .GetString()!;
            Assert.Equal(500, longCellValue.Length);
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

    private static async Task<Guid> RecordAsync(
        SqliteKustoRecordedSessionStore store,
        KustoRecordingPeriod period,
        string queryText,
        IReadOnlyList<KustoResultRow> rows,
        DateTimeOffset startedAtUtc)
    {
        Guid executionId = await store.BeginExecutionAsync(new KustoRecordedExecutionStart(
            period.Id,
            Guid.NewGuid(),
            "Synthetic query",
            new KustoQueryRequest(
                new Uri("https://mock.kusto.example/"),
                "SyntheticSecurity",
                queryText),
            startedAtUtc,
            [],
            null));
        KustoResultTable table = new(
            "PrimaryResult",
            [
                new KustoResultColumn("Indicator", "string"),
                new KustoResultColumn("Detail", "string"),
            ],
            rows);
        await store.CompleteExecutionAsync(
            executionId,
            new KustoRecordedExecutionCompletion(
                KustoRecordedExecutionStatus.Succeeded,
                startedAtUtc.AddSeconds(1),
                new KustoQueryResult([table], TimeSpan.FromSeconds(1)),
                null));
        return executionId;
    }
}
