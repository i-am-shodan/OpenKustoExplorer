using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Application.Sessions;
using OpenKustoExplorer.Infrastructure.Sessions;
using OpenKustoExplorer.Portable.Sessions;

namespace OpenKustoExplorer.Infrastructure.Tests.Sessions;

/// <summary>
/// Verifies portable recorded-session archive behavior.
/// </summary>
public sealed class KustoRecordedSessionArchiveServiceTests
{
    /// <summary>
    /// Verifies a complete session can be imported repeatedly as independent copies.
    /// </summary>
    /// <returns>A task that completes after the archive round trip.</returns>
    [Fact]
    public async Task ExportAndImportRoundTripPreservesEvidenceWithFreshIds()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "recorded-sessions.db");

        try
        {
            using SqliteKustoRecordedSessionStore store = new(filePath);
            Guid sourceSessionId = await CreateCompleteSessionAsync(store);
            KustoRecordedSession source = Assert.IsType<KustoRecordedSession>(
                await store.GetSessionAsync(sourceSessionId));
            KustoRecordedSessionArchiveService service = new(store, TimeProvider.System);
            using MemoryStream archiveStream = new();

            await service.ExportAsync(sourceSessionId, archiveStream);

            Assert.True(archiveStream.Length > 0);
            archiveStream.Position = 0;
            using (ZipArchive archive = new(archiveStream, ZipArchiveMode.Read, leaveOpen: true))
            {
                Assert.Equal(
                    [
                        KustoRecordedSessionArchiveService.ManifestEntryName,
                        KustoRecordedSessionArchiveService.SessionEntryName,
                    ],
                    archive.Entries.Select(entry => entry.FullName).ToArray());
            }

            archiveStream.Position = 0;
            KustoRecordedSessionSummary firstSummary = await service.ImportCopyAsync(archiveStream);
            archiveStream.Position = 0;
            KustoRecordedSessionSummary secondSummary = await service.ImportCopyAsync(archiveStream);

            Assert.NotEqual(sourceSessionId, firstSummary.Id);
            Assert.NotEqual(firstSummary.Id, secondSummary.Id);
            Assert.Equal("C2 investigation (imported)", firstSummary.Name);
            Assert.Equal("C2 investigation (imported 2)", secondSummary.Name);
            Assert.Equal(3, (await store.GetSessionsAsync()).Count);
            KustoRecordedSession imported = Assert.IsType<KustoRecordedSession>(
                await store.GetSessionAsync(firstSummary.Id));
            AssertEquivalentSession(source, imported);

            KustoRecordedInterest manualInterest = Assert.Single(
                imported.Interests,
                interest => interest.Source == KustoRecordedInterestSource.ManualCell);
            Guid importedMarkId = Assert.IsType<Guid>(manualInterest.MarkId);
            await store.RemoveMarkAsync(importedMarkId);

            KustoRecordedSession afterRemoval = Assert.IsType<KustoRecordedSession>(
                await store.GetSessionAsync(firstSummary.Id));
            Assert.DoesNotContain(afterRemoval.Interests, interest => interest.MarkId == importedMarkId);
            Assert.Single(afterRemoval.Marks);
            Assert.Equal(2, source.Marks.Count);
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies an open recording cannot be exported as finalized evidence.
    /// </summary>
    /// <returns>A task that completes after export is rejected.</returns>
    [Fact]
    public async Task ExportRejectsActiveRecording()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "recorded-sessions.db");

        try
        {
            using SqliteKustoRecordedSessionStore store = new(filePath);
            KustoRecordingPeriod period = await store.CreateSessionAsync(
                "Active investigation",
                DateTimeOffset.UtcNow);
            KustoRecordedSessionArchiveService service = new(store, TimeProvider.System);
            using MemoryStream archiveStream = new();

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.ExportAsync(period.SessionId, archiveStream));

            Assert.Contains("Stop the recording", exception.Message, StringComparison.Ordinal);
            Assert.Equal(0, archiveStream.Length);
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies an archive from an unsupported format version is rejected without adding a session.
    /// </summary>
    /// <returns>A task that completes after import is rejected.</returns>
    [Fact]
    public async Task ImportRejectsUnsupportedVersionWithoutMutation()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "recorded-sessions.db");

        try
        {
            using SqliteKustoRecordedSessionStore store = new(filePath);
            Guid sessionId = await CreateCompleteSessionAsync(store);
            KustoRecordedSession session = Assert.IsType<KustoRecordedSession>(
                await store.GetSessionAsync(sessionId));
            using MemoryStream archiveStream = await CreateArchiveAsync(session, version: 99);
            KustoRecordedSessionArchiveService service = new(store, TimeProvider.System);

            InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
                () => service.ImportCopyAsync(archiveStream));

            Assert.Contains("version 99", exception.Message, StringComparison.Ordinal);
            Assert.Single(await store.GetSessionsAsync());
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies an archive missing its session payload is rejected.
    /// </summary>
    /// <returns>A task that completes after import is rejected.</returns>
    [Fact]
    public async Task ImportRejectsMissingSessionEntry()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "recorded-sessions.db");

        try
        {
            using SqliteKustoRecordedSessionStore store = new(filePath);
            Guid sessionId = await CreateCompleteSessionAsync(store);
            KustoRecordedSession session = Assert.IsType<KustoRecordedSession>(
                await store.GetSessionAsync(sessionId));
            using MemoryStream archiveStream = await CreateArchiveAsync(session, includeSession: false);
            KustoRecordedSessionArchiveService service = new(store, TimeProvider.System);

            InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
                () => service.ImportCopyAsync(archiveStream));

            Assert.Contains("must contain only", exception.Message, StringComparison.Ordinal);
            Assert.Single(await store.GetSessionsAsync());
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies the manifest cannot claim a different source session.
    /// </summary>
    /// <returns>A task that completes after import is rejected.</returns>
    [Fact]
    public async Task ImportRejectsManifestMismatch()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "recorded-sessions.db");

        try
        {
            using SqliteKustoRecordedSessionStore store = new(filePath);
            Guid sessionId = await CreateCompleteSessionAsync(store);
            KustoRecordedSession session = Assert.IsType<KustoRecordedSession>(
                await store.GetSessionAsync(sessionId));
            using MemoryStream archiveStream = await CreateArchiveAsync(
                session,
                manifestSessionId: Guid.NewGuid());
            KustoRecordedSessionArchiveService service = new(store, TimeProvider.System);

            InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
                () => service.ImportCopyAsync(archiveStream));

            Assert.Contains("does not match", exception.Message, StringComparison.Ordinal);
            Assert.Single(await store.GetSessionsAsync());
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies invalid result coordinates are rejected before database mutation.
    /// </summary>
    /// <returns>A task that completes after import is rejected.</returns>
    [Fact]
    public async Task ImportRejectsUnknownCoordinateWithoutMutation()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "recorded-sessions.db");

        try
        {
            using SqliteKustoRecordedSessionStore store = new(filePath);
            Guid sessionId = await CreateCompleteSessionAsync(store);
            KustoRecordedSession source = Assert.IsType<KustoRecordedSession>(
                await store.GetSessionAsync(sessionId));
            KustoRecordedSession invalid = new(
                source.Summary,
                source.Periods,
                source.Executions,
                source.Interests,
                source.Marks,
                [
                    new KustoChainEndpoint(
                        source.Summary.Id,
                        KustoChainEndpointRole.Start,
                        new KustoRecordedValueCoordinate(Guid.NewGuid(), 0, 0, 0)),
                ]);
            using MemoryStream archiveStream = await CreateArchiveAsync(invalid);
            KustoRecordedSessionArchiveService service = new(store, TimeProvider.System);

            InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
                () => service.ImportCopyAsync(archiveStream));

            Assert.Contains("unknown result table", exception.Message, StringComparison.Ordinal);
            Assert.Single(await store.GetSessionsAsync());
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies exceeding database capacity rolls back every imported row.
    /// </summary>
    /// <returns>A task that completes after the import transaction rolls back.</returns>
    [Fact]
    public async Task ImportCapacityFailureRollsBackSession()
    {
        string directoryPath = CreateTemporaryDirectory();
        string sourcePath = Path.Combine(directoryPath, "source.db");
        string targetPath = Path.Combine(directoryPath, "target.db");

        try
        {
            using MemoryStream archiveStream = new();
            using (SqliteKustoRecordedSessionStore sourceStore = new(sourcePath))
            {
                Guid sourceSessionId = await CreateLargeSessionAsync(sourceStore);
                KustoRecordedSessionArchiveService sourceService = new(sourceStore, TimeProvider.System);
                await sourceService.ExportAsync(sourceSessionId, archiveStream);
            }

            long emptyDatabaseBytes;
            using (SqliteKustoRecordedSessionStore bootstrapStore = new(targetPath))
            {
                Assert.Empty(await bootstrapStore.GetSessionsAsync());
                emptyDatabaseBytes = ReadDatabaseUsedBytes(targetPath);
            }

            using SqliteKustoRecordedSessionStore targetStore = new(
                targetPath,
                maximumSessionCount: 10,
                maximumExecutionsPerSession: 10,
                maximumDatabaseBytes: emptyDatabaseBytes + 4096,
                maximumResultBytesPerExecution: SqliteKustoRecordedSessionStore.MaximumRecordedResultBytesPerExecution);
            KustoRecordedSessionArchiveService targetService = new(targetStore, TimeProvider.System);
            archiveStream.Position = 0;

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => targetService.ImportCopyAsync(archiveStream));

            Assert.Contains("storage capacity", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(await targetStore.GetSessionsAsync());
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    private static async Task<Guid> CreateCompleteSessionAsync(SqliteKustoRecordedSessionStore store)
    {
        DateTimeOffset startedAtUtc = new(2026, 9, 18, 10, 0, 0, TimeSpan.Zero);
        KustoRecordingPeriod firstPeriod = await store.CreateSessionAsync("C2 investigation", startedAtUtc);
        string queryText = "OutboundBrowsing | where url == \"https://malware.example.test/payload\"";
        KustoRecordedRelationDescriptor relation = new(
            "OutboundBrowsing",
            true,
            [
                new KustoSourceColumnLineage("url", "url"),
                new KustoSourceColumnLineage("src_ip", "src_ip"),
                new KustoSourceColumnLineage("nullable", "nullable"),
            ]);
        Guid successfulExecutionId = await store.BeginExecutionAsync(new KustoRecordedExecutionStart(
            firstPeriod.Id,
            Guid.NewGuid(),
            "Outbound browsing",
            new KustoQueryRequest(
                new Uri("https://mock.kusto.example/"),
                "SyntheticSecurity",
                queryText),
            startedAtUtc,
            [
                new KustoPredicateInterest(
                    "url",
                    "string",
                    "https://malware.example.test/payload",
                    queryText.IndexOf('"'),
                    "\"https://malware.example.test/payload\"".Length),
            ],
            relation));
        KustoResultTable primary = new(
            "PrimaryResult",
            [
                new KustoResultColumn("url", "string"),
                new KustoResultColumn("src_ip", "string"),
                new KustoResultColumn("nullable", "string"),
            ],
            [
                new KustoResultRow(
                [
                    new KustoResultValue(
                        "https://malware.example.test/payload",
                        "\"https://malware.example.test/payload\"",
                        false),
                    new KustoResultValue("192.0.2.56", null, false),
                    new KustoResultValue(string.Empty, "null", true),
                ]),
            ]);
        await store.CompleteExecutionAsync(
            successfulExecutionId,
            new KustoRecordedExecutionCompletion(
                KustoRecordedExecutionStatus.Succeeded,
                startedAtUtc.AddSeconds(2),
                new KustoQueryResult(
                    [primary],
                    TimeSpan.FromMilliseconds(1250),
                    completeness: KustoQueryResultCompleteness.RecordLimitReached),
                null));
        await store.RenameExecutionAsync(successfulExecutionId, "Suspicious outbound activity");
        await store.AddMarkAsync(
            firstPeriod.SessionId,
            KustoRecordedMarkKind.Cell,
            new KustoRecordedValueCoordinate(successfulExecutionId, 0, 0, 1),
            startedAtUtc.AddSeconds(3));
        await store.SetEndpointAsync(
            firstPeriod.SessionId,
            KustoChainEndpointRole.Start,
            new KustoRecordedValueCoordinate(successfulExecutionId, 0, 0, 0));
        await store.SetEndpointAsync(
            firstPeriod.SessionId,
            KustoChainEndpointRole.End,
            new KustoRecordedValueCoordinate(successfulExecutionId, 0, 0, 1));
        await store.StopRecordingAsync(firstPeriod.Id, startedAtUtc.AddMinutes(1));

        KustoRecordingPeriod secondPeriod = await store.AppendSessionAsync(
            firstPeriod.SessionId,
            startedAtUtc.AddMinutes(2));
        Guid failedExecutionId = await store.BeginExecutionAsync(new KustoRecordedExecutionStart(
            secondPeriod.Id,
            Guid.NewGuid(),
            "Failed query",
            new KustoQueryRequest(
                new Uri("https://mock.kusto.example/"),
                "SyntheticSecurity",
                "MissingTable | take 1"),
            startedAtUtc.AddMinutes(2),
            [],
            null));
        await store.CompleteExecutionAsync(
            failedExecutionId,
            new KustoRecordedExecutionCompletion(
                KustoRecordedExecutionStatus.Failed,
                startedAtUtc.AddMinutes(2).AddSeconds(1),
                null,
                "Synthetic query failure"));
        await store.StopRecordingAsync(secondPeriod.Id, startedAtUtc.AddMinutes(3));
        return firstPeriod.SessionId;
    }

    private static async Task<Guid> CreateLargeSessionAsync(SqliteKustoRecordedSessionStore store)
    {
        DateTimeOffset startedAtUtc = new(2026, 9, 18, 11, 0, 0, TimeSpan.Zero);
        KustoRecordingPeriod period = await store.CreateSessionAsync("Large investigation", startedAtUtc);
        Guid executionId = await store.BeginExecutionAsync(new KustoRecordedExecutionStart(
            period.Id,
            Guid.NewGuid(),
            "Large result",
            new KustoQueryRequest(
                new Uri("https://mock.kusto.example/"),
                "SyntheticSecurity",
                "print Payload"),
            startedAtUtc,
            [],
            null));
        KustoResultTable table = new(
            "PrimaryResult",
            [new KustoResultColumn("Payload", "string")],
            [new KustoResultRow([new string('x', 100_000)])]);
        await store.CompleteExecutionAsync(
            executionId,
            new KustoRecordedExecutionCompletion(
                KustoRecordedExecutionStatus.Succeeded,
                startedAtUtc.AddSeconds(1),
                new KustoQueryResult([table], TimeSpan.FromSeconds(1)),
                null));
        await store.StopRecordingAsync(period.Id, startedAtUtc.AddMinutes(1));
        return period.SessionId;
    }

    private static async Task<MemoryStream> CreateArchiveAsync(
        KustoRecordedSession session,
        int version = KustoRecordedSessionArchiveJson.CurrentVersion,
        Guid? manifestSessionId = null,
        bool includeSession = true)
    {
        MemoryStream stream = new();
        using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            ZipArchiveEntry manifestEntry = archive.CreateEntry(
                KustoRecordedSessionArchiveService.ManifestEntryName,
                CompressionLevel.Optimal);
            await using (Stream manifestStream = await manifestEntry.OpenAsync())
            {
                using Utf8JsonWriter writer = new(manifestStream);
                writer.WriteStartObject();
                writer.WriteString("format", KustoRecordedSessionArchiveJson.FormatName);
                writer.WriteNumber("version", version);
                writer.WriteString("exportedAtUtc", DateTimeOffset.UtcNow);
                writer.WriteString("sourceSessionId", manifestSessionId ?? session.Summary.Id);
                writer.WriteString("sourceSessionName", session.Summary.Name);
                writer.WriteEndObject();
            }

            if (includeSession)
            {
                ZipArchiveEntry sessionEntry = archive.CreateEntry(
                    KustoRecordedSessionArchiveService.SessionEntryName,
                    CompressionLevel.Optimal);
                await using Stream sessionStream = await sessionEntry.OpenAsync();
                KustoRecordedSessionArchiveJson.WriteSession(sessionStream, session);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static long ReadDatabaseUsedBytes(string filePath)
    {
        using SqliteConnection connection = new($"Data Source={filePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA page_count;";
        long pageCount = (long)(command.ExecuteScalar() ?? 0L);
        command.CommandText = "PRAGMA freelist_count;";
        long freePageCount = (long)(command.ExecuteScalar() ?? 0L);
        command.CommandText = "PRAGMA page_size;";
        long pageSize = (long)(command.ExecuteScalar() ?? 0L);
        return Math.Max(0, pageCount - freePageCount) * pageSize;
    }

    private static void AssertEquivalentSession(KustoRecordedSession source, KustoRecordedSession imported)
    {
        Assert.NotEqual(source.Summary.Id, imported.Summary.Id);
        Assert.Equal(source.Summary.CreatedAtUtc, imported.Summary.CreatedAtUtc);
        Assert.Equal(source.Summary.LastUpdatedAtUtc, imported.Summary.LastUpdatedAtUtc);
        Assert.Equal(source.Periods.Count, imported.Periods.Count);
        Assert.All(imported.Periods, period => Assert.Equal(imported.Summary.Id, period.SessionId));
        Assert.Equal(
            source.Periods.Select(period => (period.StartedAtUtc, period.StoppedAtUtc)),
            imported.Periods.Select(period => (period.StartedAtUtc, period.StoppedAtUtc)));

        Assert.Equal(source.Executions.Count, imported.Executions.Count);
        for (int index = 0; index < source.Executions.Count; index++)
        {
            KustoRecordedExecution sourceExecution = source.Executions[index];
            KustoRecordedExecution importedExecution = imported.Executions[index];
            Assert.NotEqual(sourceExecution.Id, importedExecution.Id);
            Assert.Equal(imported.Summary.Id, importedExecution.SessionId);
            Assert.Equal(sourceExecution.Sequence, importedExecution.Sequence);
            Assert.Equal(sourceExecution.DocumentId, importedExecution.DocumentId);
            Assert.Equal(sourceExecution.DocumentTitle, importedExecution.DocumentTitle);
            Assert.Equal(sourceExecution.DisplayName, importedExecution.DisplayName);
            Assert.Equal(sourceExecution.ClusterUri, importedExecution.ClusterUri);
            Assert.Equal(sourceExecution.DatabaseName, importedExecution.DatabaseName);
            Assert.Equal(sourceExecution.QueryText, importedExecution.QueryText);
            Assert.Equal(sourceExecution.StartedAtUtc, importedExecution.StartedAtUtc);
            Assert.Equal(sourceExecution.CompletedAtUtc, importedExecution.CompletedAtUtc);
            Assert.Equal(sourceExecution.Status, importedExecution.Status);
            Assert.Equal(sourceExecution.ErrorMessage, importedExecution.ErrorMessage);
        }

        KustoRecordedExecution sourceSuccess = source.Executions[0];
        KustoRecordedExecution importedSuccess = imported.Executions[0];
        Assert.Equal(sourceSuccess.Relation?.SourceTableName, importedSuccess.Relation?.SourceTableName);
        IEnumerable<(string ResultColumnName, string SourceColumnName)>? sourceLineage =
            sourceSuccess.Relation?.Columns.Select(
            column => (column.ResultColumnName, column.SourceColumnName));
        IEnumerable<(string ResultColumnName, string SourceColumnName)>? importedLineage =
            importedSuccess.Relation?.Columns.Select(
            column => (column.ResultColumnName, column.SourceColumnName));
        Assert.Equal(sourceLineage, importedLineage);
        Assert.Equal(sourceSuccess.Result?.Completeness, importedSuccess.Result?.Completeness);
        Assert.Equal(sourceSuccess.Result?.Duration, importedSuccess.Result?.Duration);
        IEnumerable<(string DisplayText, string? RawJson, bool IsNull)>? sourceValues =
            sourceSuccess.Result?.Tables[0].Rows[0].ResultValues.Select(
            value => (value.DisplayText, value.RawJson, value.IsNull));
        IEnumerable<(string DisplayText, string? RawJson, bool IsNull)>? importedValues =
            importedSuccess.Result?.Tables[0].Rows[0].ResultValues.Select(
            value => (value.DisplayText, value.RawJson, value.IsNull));
        Assert.Equal(sourceValues, importedValues);

        Assert.Equal(source.Interests.Count, imported.Interests.Count);
        Assert.Equal(source.Marks.Count, imported.Marks.Count);
        Assert.Equal(source.Endpoints.Count, imported.Endpoints.Count);
        Assert.All(imported.Interests, interest => Assert.Equal(imported.Summary.Id, interest.SessionId));
        Assert.All(imported.Marks, mark => Assert.Equal(imported.Summary.Id, mark.SessionId));
        Assert.All(imported.Endpoints, endpoint => Assert.Equal(imported.Summary.Id, endpoint.SessionId));
        Assert.All(
            imported.Marks,
            mark => Assert.Contains(imported.Interests, interest => interest.MarkId == mark.Id));
    }

    private static string CreateTemporaryDirectory()
    {
        string directoryPath = Path.Combine(
            Path.GetTempPath(),
            $"OpenKustoExplorer-SessionArchive-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        return directoryPath;
    }

    private static void DeleteTemporaryDirectory(string directoryPath)
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directoryPath))
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }
}
