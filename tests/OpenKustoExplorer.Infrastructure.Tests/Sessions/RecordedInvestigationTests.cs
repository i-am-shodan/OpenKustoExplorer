using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenKustoExplorer.Application.Assistance;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Application.Language;
using OpenKustoExplorer.Application.Sessions;
using OpenKustoExplorer.Domain.Schema;
using OpenKustoExplorer.Infrastructure.Assistance;
using OpenKustoExplorer.Infrastructure.Sessions;
using OpenKustoExplorer.Portable.Sessions;

namespace OpenKustoExplorer.Infrastructure.Tests.Sessions;

/// <summary>
/// Verifies synthetic recorded investigation and query-chain workflows.
/// </summary>
public sealed class RecordedInvestigationTests
{
    private const string EmailAddress = "analyst@example.test";
    private const string Hostname = "SYNTHETIC-HOST";
    private const string IpAddress = "192.0.2.56";
    private const string MalwareUrl = "https://malware.example.test/payload";
    private const string Username = "example-user";

    /// <summary>
    /// Verifies predicate-only inputs compose through expansion and a lookup join.
    /// </summary>
    /// <returns>A task that completes after the synthetic recorded chain is generated.</returns>
    [Fact]
    public async Task RecordedSyntheticInvestigationBuildsIpToUsernameQuery()
    {
        const string Fingerprint = "SYNTHETIC-FINGERPRINT-0001";
        const string DocumentationIp = "192.0.2.10";
        const string SyntheticUsername = "synthetic-user";
        const string IpToFingerprintQuery = """
            SyntheticNetworkEvents
            | where ClientIp == "192.0.2.10"
            | where ProtocolFingerprint != ""
            | summarize by ProtocolFingerprint
            """;
        const string FingerprintToUsernameQuery = """
            SyntheticNetworkEvents
            | where ['ProtocolFingerprint'] == 'SYNTHETIC-FINGERPRINT-0001'
            | mv-expand PublicKeys
            | summarize by tostring(PublicKeys)
            | join SyntheticPublicKeyOwners on $left.PublicKeys == $right.PublicKey
            """;
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "recorded-sessions.db");
        KustoDatabaseSchema schema = CreateSyntheticConversionSchema();
        KustoPredicateInterestExtractor interestExtractor = new();
        KustoRecordedRelationExtractor relationExtractor = new();
        DateTimeOffset startedAtUtc = new(2026, 9, 7, 15, 0, 0, TimeSpan.Zero);

        try
        {
            using SqliteKustoRecordedSessionStore store = new(filePath);
            KustoRecordingPeriod period = await store.CreateSessionAsync("Synthetic conversion", startedAtUtc);
            RecordedQuery seed = await RecordAsync(
                store,
                period,
                schema,
                interestExtractor,
                relationExtractor,
                $"SyntheticNetworkEvents | where ClientIp == '{DocumentationIp}'",
                CreateTable("PrimaryResult", ["ClientIp"], [[DocumentationIp]]),
                startedAtUtc,
                false);
            RecordedQuery usernameLookup = await RecordAsync(
                store,
                period,
                schema,
                interestExtractor,
                relationExtractor,
                FingerprintToUsernameQuery,
                CreateTable(
                    "PrimaryResult",
                    ["PublicKeys", "PublicKey", "Username"],
                    [["ssh-rsa SYNTHETIC", "ssh-rsa SYNTHETIC", SyntheticUsername]]),
                startedAtUtc.AddMinutes(1),
                false);
            RecordedQuery ipLookup = await RecordAsync(
                store,
                period,
                schema,
                interestExtractor,
                relationExtractor,
                IpToFingerprintQuery,
                CreateTable("PrimaryResult", ["ProtocolFingerprint"], [[Fingerprint]]),
                startedAtUtc.AddMinutes(2),
                false);
            KustoRecordedValueCoordinate start = new(seed.ExecutionId, 0, 0, 0);
            KustoRecordedValueCoordinate end = new(usernameLookup.ExecutionId, 0, 0, 2);
            await store.SetEndpointAsync(period.SessionId, KustoChainEndpointRole.Start, start);
            await store.SetEndpointAsync(period.SessionId, KustoChainEndpointRole.End, end);
            await store.StopRecordingAsync(period.Id, startedAtUtc.AddMinutes(3));

            KustoRecordedSession session = Assert.IsType<KustoRecordedSession>(
                await store.GetSessionAsync(period.SessionId));
            Assert.All(session.Executions, execution => Assert.Null(execution.Relation));
            KustoRecordedChainSearcher searcher = new(store);
            KustoQueryChain chain = Assert.IsType<KustoQueryChain>(await searcher.FindAsync(
                period.SessionId,
                start,
                end,
                schema));
            Assert.Equal(2, chain.Pivots.Count);
            Assert.Equal(
                [KustoPivotEvidenceKind.PredicateToPredicate, KustoPivotEvidenceKind.PredicateToManual],
                chain.Pivots.Select(pivot => pivot.Kind));
            KustoRecordedRelationPlanner planner = new();
            KustoRelationalChainPlan plan = Assert.IsType<KustoRelationalChainPlan>(
                planner.CreatePlan(session, chain));
            Assert.All(plan.Steps, step => Assert.True(step.UsesQueryPipeline));
            Assert.Equal(
                [ipLookup.ExecutionId, usernameLookup.ExecutionId],
                plan.Steps.Select(step => step.ExecutionId));
            KustoRecordedChainQueryGenerator generator = new();

            KustoGeneratedChainQuery generated = generator.Generate(plan, schema);

            Assert.True(generated.Succeeded, string.Join(Environment.NewLine, generated.Diagnostics));
            const string ExpectedQuery = """
                let chain_input = '192.0.2.10';
                let chain_step_1 = (
                    SyntheticNetworkEvents
                    | where ClientIp == chain_input
                    | where ProtocolFingerprint != ""
                    | project ClientIp, ProtocolFingerprint
                );
                let chain_step_2 = (
                    SyntheticNetworkEvents
                    | project PublicKeys, ProtocolFingerprint
                    | mv-expand PublicKeys
                    | extend PublicKeys = tostring(PublicKeys)
                    | join SyntheticPublicKeyOwners on $left.PublicKeys == $right.PublicKey
                    | project ProtocolFingerprint, Username
                );
                chain_step_1
                | join kind=inner (chain_step_2) on $left.ProtocolFingerprint == $right.ProtocolFingerprint
                | project ClientIp, Username
                """;
            Assert.Equal(ExpectedQuery, generated.QueryText.ReplaceLineEndings("\n"));
            Assert.DoesNotContain(Fingerprint, generated.QueryText, StringComparison.Ordinal);

            KustoRecordedValueCoordinate via = chain.Pivots[0].OutputCoordinate;
            string copilotResult = await CopilotRecordedSessionTools.GenerateChainQueryAsync(
                store,
                searcher,
                planner,
                generator,
                new KustoCopilotRecordedSessionScope(period.SessionId, schema),
                $"{start.ExecutionId:D}/0/0/0",
                $"{via.ExecutionId:D}/{via.TableOrdinal}/{via.RowOrdinal}/{via.ColumnOrdinal}",
                $"{end.ExecutionId:D}/0/0/2",
                CancellationToken.None);
            using JsonDocument copilotDocument = JsonDocument.Parse(copilotResult);
            JsonElement copilotRoot = copilotDocument.RootElement;
            Assert.True(
                copilotRoot.TryGetProperty("succeeded", out JsonElement succeeded)
                    && succeeded.GetBoolean(),
                copilotResult);
            Assert.True(copilotRoot.GetProperty("usedRequiredIntermediate").GetBoolean());
            Assert.Equal(
                ExpectedQuery,
                copilotRoot.GetProperty("queryText").GetString()!.ReplaceLineEndings("\n"));
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies a five-query investigation yields the minimal URL-to-host relation plan and runnable KQL.
    /// </summary>
    /// <returns>A task that completes after the recorded investigation is analyzed.</returns>
    [Fact]
    public async Task RecordedInvestigationBuildsMinimalUrlToHostQuery()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "recorded-sessions.db");
        KustoDatabaseSchema schema = CreateSchema();
        DateTimeOffset startedAtUtc = new(2026, 9, 6, 14, 0, 0, TimeSpan.Zero);

        try
        {
            using SqliteKustoRecordedSessionStore store = new(filePath);
            RecordedInvestigation investigation = await RecordMinimalUrlToHostInvestigationAsync(
                store,
                schema,
                startedAtUtc);
            await VerifyMinimalUrlToHostQueryAsync(store, schema, investigation);
        }
        finally
        {
            DeleteTemporaryDirectory(directoryPath);
        }
    }

    /// <summary>
    /// Verifies the Browser JSON session aggregate retains all data required for URL-to-host KQL generation.
    /// </summary>
    /// <returns>A task that completes after the reloaded recorded investigation is analyzed.</returns>
    [Fact]
    public async Task BrowserRecordedInvestigationBuildsMinimalUrlToHostQueryAfterReload()
    {
        MemoryRecordedSessionSnapshotStore snapshotStore = new();
        KustoDatabaseSchema schema = CreateSchema();
        DateTimeOffset startedAtUtc = new(2026, 9, 6, 14, 0, 0, TimeSpan.Zero);
        RecordedInvestigation investigation;

        using (JsonKustoRecordedSessionStore store = await JsonKustoRecordedSessionStore.CreateAsync(snapshotStore))
        {
            investigation = await RecordMinimalUrlToHostInvestigationAsync(store, schema, startedAtUtc);
        }

        using JsonKustoRecordedSessionStore reloaded = await JsonKustoRecordedSessionStore.CreateAsync(snapshotStore);
        await VerifyMinimalUrlToHostQueryAsync(reloaded, schema, investigation);
    }

    private static async Task<RecordedInvestigation> RecordMinimalUrlToHostInvestigationAsync(
        IKustoRecordedSessionStore store,
        KustoDatabaseSchema schema,
        DateTimeOffset startedAtUtc)
    {
        KustoPredicateInterestExtractor interestExtractor = new();
        KustoRecordedRelationExtractor relationExtractor = new();
        KustoRecordingPeriod period = await store.CreateSessionAsync(
            "Malware C2 investigation",
            startedAtUtc);
        RecordedQuery first = await RecordAsync(
            store,
            period,
            schema,
            interestExtractor,
            relationExtractor,
            "OutboundBrowsing | where url == \"" + MalwareUrl + "\"",
            CreateTable(
                "PrimaryResult",
                ["url", "src_ip"],
                [
                    ["https://benign.example.test/", "192.0.2.20"],
                    [MalwareUrl, IpAddress],
                ]),
            startedAtUtc);
        RecordedQuery authentication = await RecordAsync(
            store,
            period,
            schema,
            interestExtractor,
            relationExtractor,
            "AuthenticationEvents | where src_ip == \"" + IpAddress + "\"",
            CreateTable(
                "PrimaryResult",
                ["src_ip", "username"],
                [
                    ["192.0.2.20", "benign-user"],
                    [IpAddress, Username],
                ]),
            startedAtUtc.AddMinutes(1));
        RecordedQuery employeeByUsername = await RecordAsync(
            store,
            period,
            schema,
            interestExtractor,
            relationExtractor,
            "Employees | where username == \"" + Username + "\"",
            CreateEmployeesTable(),
            startedAtUtc.AddMinutes(2));
        RecordedQuery emptyEmail = await RecordAsync(
            store,
            period,
            schema,
            interestExtractor,
            relationExtractor,
            "Email | where recipient == \"" + EmailAddress + "\"",
            CreateTable("PrimaryResult", ["recipient", "sender"], []),
            startedAtUtc.AddMinutes(3));
        RecordedQuery employeeByEmail = await RecordAsync(
            store,
            period,
            schema,
            interestExtractor,
            relationExtractor,
            "Employees | where email_addr == \"" + EmailAddress + "\"",
            CreateEmployeesTable(),
            startedAtUtc.AddMinutes(4));
        KustoRecordedValueCoordinate hostnameCoordinate = new(employeeByEmail.ExecutionId, 0, 1, 3);
        KustoRecordedValueCoordinate urlCoordinate = new(first.ExecutionId, 0, 1, 0);
        await store.SetEndpointAsync(period.SessionId, KustoChainEndpointRole.Start, urlCoordinate);
        await store.SetEndpointAsync(period.SessionId, KustoChainEndpointRole.End, hostnameCoordinate);
        await store.StopRecordingAsync(period.Id, startedAtUtc.AddMinutes(5));
        return new RecordedInvestigation(
            period.SessionId,
            authentication.ExecutionId,
            employeeByUsername.ExecutionId,
            emptyEmail.ExecutionId,
            urlCoordinate,
            hostnameCoordinate);
    }

    private static async Task VerifyMinimalUrlToHostQueryAsync(
        IKustoRecordedSessionStore store,
        KustoDatabaseSchema schema,
        RecordedInvestigation investigation)
    {
        KustoRecordedSession session = Assert.IsType<KustoRecordedSession>(
            await store.GetSessionAsync(investigation.SessionId));
        Assert.Equal(5, session.Executions.Count);
        Assert.Empty(Assert.Single(session.Executions.Single(
            execution => execution.Id == investigation.EmptyEmailExecutionId).Result!.Tables).Rows);
        Assert.Contains(
            session.Interests,
            interest => interest.DeclaredExecutionId == investigation.EmployeeByUsernameExecutionId
                && interest.Identity.CanonicalValue == Username);
        KustoResultRow authenticationRow = session.Executions.Single(
            execution => execution.Id == investigation.AuthenticationExecutionId).Result!.Tables[0].Rows[1];
        KustoRecordedValueIdentity earlierUsername = KustoRecordedValueCanonicalizer.Create(
            "string",
            authenticationRow.ResultValues[1]);
        Assert.Contains(session.Interests, interest => interest.Identity.Equals(earlierUsername));

        KustoRecordedChainSearcher searcher = new(store);
        KustoQueryChain chain = Assert.IsType<KustoQueryChain>(await searcher.FindAsync(
            investigation.SessionId,
            investigation.UrlCoordinate,
            investigation.HostnameCoordinate,
            schema));
        Assert.Equal(3, chain.TotalCost);
        Assert.Equal(2, chain.Pivots.Count);
        Assert.Equal(["OutboundBrowsing", "Employees"], chain.Pivots.Select(pivot => pivot.SourceTableName));
        Assert.Equal(KustoPivotEvidenceKind.PredicateToManual, chain.Pivots[0].Kind);
        Assert.Equal(KustoPivotEvidenceKind.PriorInterestToManual, chain.Pivots[1].Kind);
        Assert.Equal("src_ip", chain.Pivots[0].OutputSourceColumnName);
        Assert.Equal("ip_addr", chain.Pivots[1].InputSourceColumnName);

        KustoRecordedRelationPlanner planner = new();
        KustoRelationalChainPlan plan = Assert.IsType<KustoRelationalChainPlan>(
            planner.CreatePlan(session, chain));
        Assert.Equal(["OutboundBrowsing", "Employees"], plan.Steps.Select(step => step.SourceTableName));
        Assert.DoesNotContain(plan.Steps, step => step.ExecutionId == investigation.AuthenticationExecutionId);
        Assert.DoesNotContain(plan.Steps, step => step.ExecutionId == investigation.EmptyEmailExecutionId);

        KustoGeneratedChainQuery generated = new KustoRecordedChainQueryGenerator().Generate(plan, schema);
        Assert.True(generated.Succeeded, string.Join(Environment.NewLine, generated.Diagnostics));
        Assert.Contains("let chain_input = '" + MalwareUrl + "';", generated.QueryText, StringComparison.Ordinal);
        Assert.Contains("OutboundBrowsing", generated.QueryText, StringComparison.Ordinal);
        Assert.Contains("join kind=inner", generated.QueryText, StringComparison.Ordinal);
        Assert.Contains("Employees", generated.QueryText, StringComparison.Ordinal);
        Assert.Contains("$left.src_ip == $right.ip_addr", generated.QueryText, StringComparison.Ordinal);
        Assert.Contains("project url, hostname", generated.QueryText, StringComparison.Ordinal);
        Assert.DoesNotContain("AuthenticationEvents", generated.QueryText, StringComparison.Ordinal);
        Assert.DoesNotContain("Email |", generated.QueryText, StringComparison.Ordinal);
        Assert.DoesNotContain("username ==", generated.QueryText, StringComparison.Ordinal);
        Assert.DoesNotContain("email_addr ==", generated.QueryText, StringComparison.Ordinal);
    }

    private static async Task<RecordedQuery> RecordAsync(
        IKustoRecordedSessionStore store,
        KustoRecordingPeriod period,
        KustoDatabaseSchema schema,
        KustoPredicateInterestExtractor interestExtractor,
        KustoRecordedRelationExtractor relationExtractor,
        string queryText,
        KustoResultTable table,
        DateTimeOffset startedAtUtc,
        bool persistRelation = true)
    {
        IReadOnlyList<KustoPredicateInterest> interests = interestExtractor.Extract(queryText, schema);
        KustoRecordedRelationDescriptor? relation = persistRelation
            ? relationExtractor.Extract(queryText, schema)
            : null;
        Guid executionId = await store.BeginExecutionAsync(new KustoRecordedExecutionStart(
            period.Id,
            Guid.NewGuid(),
            "Security research",
            new KustoQueryRequest(new Uri($"https://{schema.ClusterName}/"), schema.DatabaseName, queryText),
            startedAtUtc,
            interests,
            relation));
        await store.CompleteExecutionAsync(
            executionId,
            new KustoRecordedExecutionCompletion(
                KustoRecordedExecutionStatus.Succeeded,
                startedAtUtc.AddSeconds(1),
                new KustoQueryResult([table], TimeSpan.FromSeconds(1)),
                null));
        return new RecordedQuery(executionId);
    }

    private static KustoResultTable CreateEmployeesTable()
    {
        return CreateTable(
            "PrimaryResult",
            ["username", "email_addr", "ip_addr", "hostname"],
            [
                ["benign-user", "benign@example.test", "192.0.2.20", "SAFE-MACHINE"],
                [Username, EmailAddress, IpAddress, Hostname],
            ]);
    }

    private static KustoResultTable CreateTable(
        string name,
        IReadOnlyList<string> columnNames,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        KustoResultColumn[] columns = columnNames
            .Select(columnName => new KustoResultColumn(columnName, "string"))
            .ToArray();
        KustoResultRow[] resultRows = rows
            .Select(row => new KustoResultRow(row.Select(value => new KustoResultValue(
                value,
                JsonSerializer.Serialize(value),
                false))))
            .ToArray();
        return new KustoResultTable(name, columns, resultRows);
    }

    private static KustoDatabaseSchema CreateSchema()
    {
        return new KustoDatabaseSchema(
            "mock.kusto.example",
            "SyntheticSecurity",
            [
                CreateTableSchema("OutboundBrowsing", "url", "src_ip"),
                CreateTableSchema("AuthenticationEvents", "src_ip", "username"),
                CreateTableSchema("Employees", "username", "email_addr", "ip_addr", "hostname"),
                CreateTableSchema("Email", "recipient", "sender"),
            ]);
    }

    private static KustoDatabaseSchema CreateSyntheticConversionSchema()
    {
        return new KustoDatabaseSchema(
            "mock.kusto.example",
            "SyntheticSecurity",
            [
                new KustoTableSchema(
                    "SyntheticNetworkEvents",
                    [
                        new KustoColumnSchema("ClientIp", KustoScalarType.Text),
                        new KustoColumnSchema("ProtocolFingerprint", KustoScalarType.Text),
                        new KustoColumnSchema("PublicKeys", KustoScalarType.Dynamic),
                        new KustoColumnSchema("Username", KustoScalarType.Text),
                    ]),
                CreateTableSchema("SyntheticPublicKeyOwners", "PublicKey", "Username"),
            ]);
    }

    private static KustoTableSchema CreateTableSchema(string name, params string[] columnNames)
    {
        return new KustoTableSchema(
            name,
            columnNames.Select(columnName => new KustoColumnSchema(columnName, KustoScalarType.Text)));
    }

    private static string CreateTemporaryDirectory()
    {
        string directoryPath = Path.Combine(
            Path.GetTempPath(),
            $"OpenKustoExplorer-SyntheticInvestigation-{Guid.NewGuid():N}");
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

    private sealed record RecordedInvestigation(
        Guid SessionId,
        Guid AuthenticationExecutionId,
        Guid EmployeeByUsernameExecutionId,
        Guid EmptyEmailExecutionId,
        KustoRecordedValueCoordinate UrlCoordinate,
        KustoRecordedValueCoordinate HostnameCoordinate);

    private sealed record RecordedQuery(Guid ExecutionId);

    private sealed class MemoryRecordedSessionSnapshotStore : IKustoRecordedSessionSnapshotStore
    {
        private string? json;

        public Task<string?> LoadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(json);
        }

        public Task SaveAsync(string snapshotJson, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            json = snapshotJson;
            return Task.CompletedTask;
        }
    }
}
