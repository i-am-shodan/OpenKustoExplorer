using OpenKustoExplorer.Application.Documents;
using OpenKustoExplorer.Infrastructure.Documents;

namespace OpenKustoExplorer.Infrastructure.Tests.Documents;

/// <summary>
/// Verifies durable KQL document workspace persistence.
/// </summary>
public sealed class FileKustoDocumentStoreTests
{
    /// <summary>
    /// Verifies that tab order, selection, content, targets, colors, and groups round-trip through JSON.
    /// </summary>
    /// <returns>A task that completes after the workspace is reloaded.</returns>
    [Fact]
    public async Task SaveAndLoadRoundTripsDocumentWorkspace()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "documents.json");

        try
        {
            Guid firstId = Guid.NewGuid();
            Guid secondId = Guid.NewGuid();
            Uri firstCluster = new("https://first.kusto.windows.net");
            Uri secondCluster = new("https://second.kusto.windows.net");
            Guid ruleId = Guid.NewGuid();
            KustoDocumentWorkspace workspace = new(
                [
                    new KustoDocument(
                        firstId,
                        "Investigate",
                        "Events | take 10",
                        5,
                        firstCluster,
                        "Telemetry",
                        KustoDocumentTabColor.Blue,
                        "Incidents",
                        true,
                        [
                            new KustoConditionalFormatRule(
                                ruleId,
                                "Severity",
                                KustoConditionalFormatOperator.TextContains,
                                "error",
                                KustoConditionalFormatTarget.Row,
                                "#FECACA"),
                            ]),
                    new KustoDocument(
                        secondId,
                        "Monitor",
                        "Metrics | count",
                        14,
                        secondCluster,
                        "Operations",
                        KustoDocumentTabColor.Red,
                        "Operations",
                        false),
                ],
                secondId);
            FileKustoDocumentStore store = new(filePath);

            await store.SaveAsync(workspace);
            KustoDocumentWorkspace restored = store.Load();

            Assert.Equal(secondId, restored.SelectedDocumentId);
            Assert.Equal(2, restored.Documents.Count);
            Assert.Equal(["Investigate", "Monitor"], restored.Documents.Select(document => document.Title));
            Assert.Equal("Events | take 10", restored.Documents[0].Text);
            Assert.Equal(5, restored.Documents[0].CaretPosition);
            Assert.Equal(firstCluster, restored.Documents[0].ClusterUri);
            Assert.Equal("Telemetry", restored.Documents[0].DatabaseName);
            Assert.Equal(KustoDocumentTabColor.Blue, restored.Documents[0].TabColor);
            Assert.Equal("Incidents", restored.Documents[0].GroupName);
            Assert.True(restored.Documents[0].UseAlternatingRows);
            KustoConditionalFormatRule rule = Assert.Single(restored.Documents[0].ConditionalFormattingRules);
            Assert.Equal(ruleId, rule.Id);
            Assert.Equal(KustoConditionalFormatOperator.TextContains, rule.Comparison);
            Assert.Equal(KustoConditionalFormatTarget.Row, rule.Target);
            Assert.Equal("#FECACA", rule.ColorHex);
            Assert.Equal(secondCluster, restored.Documents[1].ClusterUri);
            Assert.Equal("Operations", restored.Documents[1].DatabaseName);
            Assert.Equal(KustoDocumentTabColor.Red, restored.Documents[1].TabColor);
            Assert.Equal("Operations", restored.Documents[1].GroupName);
            Assert.False(restored.Documents[1].UseAlternatingRows);
        }
        finally
        {
            Directory.Delete(directoryPath, true);
        }
    }

    /// <summary>
    /// Verifies that existing version-one workspaces without tab metadata retain default presentation values.
    /// </summary>
    [Fact]
    public void LoadDefaultsMissingTabMetadata()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "documents.json");
        Guid documentId = Guid.NewGuid();
        string json = $$"""
                        {
                            "version": 1,
                            "selectedDocumentId": "{{documentId}}",
                            "documents": [
                                {
                                    "id": "{{documentId}}",
                                    "title": "Existing query",
                                    "text": "StormEvents | count",
                                    "caretPosition": 4,
                                    "clusterUri": null,
                                    "databaseName": null
                                }
                            ]
                        }
                        """;

        try
        {
            File.WriteAllText(filePath, json);
            FileKustoDocumentStore store = new(filePath);

            KustoDocument restored = Assert.Single(store.Load().Documents);

            Assert.Equal(KustoDocumentTabColor.Default, restored.TabColor);
            Assert.Null(restored.GroupName);
            Assert.True(restored.UseAlternatingRows);
            Assert.Empty(restored.ConditionalFormattingRules);
        }
        finally
        {
            Directory.Delete(directoryPath, true);
        }
    }

    /// <summary>
    /// Verifies version-two workspaces retain their query tabs while obsolete analysis metadata is ignored.
    /// </summary>
    [Fact]
    public void LoadAcceptsVersionTwoWorkspaceWithObsoleteAnalysisMetadata()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Join(directoryPath, "documents.json");
        Guid documentId = Guid.NewGuid();
        string json = $$"""
                        {
                            "version": 2,
                            "selectedDocumentId": "{{documentId}}",
                            "documents": [
                                {
                                    "id": "{{documentId}}",
                                    "title": "Existing analysis",
                                    "text": "StormEvents | count",
                                    "caretPosition": 4,
                                    "clusterUri": null,
                                    "databaseName": null,
                                    "resultAnalysis": {
                                        "mode": "Grouped",
                                        "calculatedColumns": [],
                                        "groupColumnNames": ["State"],
                                        "aggregates": []
                                    }
                                }
                            ]
                        }
                        """;

        try
        {
            File.WriteAllText(filePath, json);
            FileKustoDocumentStore store = new(filePath);

            KustoDocument restored = Assert.Single(store.Load().Documents);

            Assert.Equal(documentId, restored.Id);
            Assert.Equal("Existing analysis", restored.Title);
            Assert.Equal("StormEvents | count", restored.Text);
        }
        finally
        {
            Directory.Delete(directoryPath, true);
        }
    }

    /// <summary>
    /// Verifies that corrupt autosave JSON does not prevent startup.
    /// </summary>
    [Fact]
    public void LoadReturnsEmptyWorkspaceForCorruptJson()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "documents.json");

        try
        {
            File.WriteAllText(filePath, "not json");
            FileKustoDocumentStore store = new(filePath);

            KustoDocumentWorkspace workspace = store.Load();

            Assert.Empty(workspace.Documents);
            Assert.Null(workspace.SelectedDocumentId);
        }
        finally
        {
            Directory.Delete(directoryPath, true);
        }
    }

    /// <summary>
    /// Verifies that a failed primary write preserves the latest workspace at the recovery path.
    /// </summary>
    /// <returns>A task that completes after the recovery copy is inspected.</returns>
    [Fact]
    public async Task SaveWritesRecoveryCopyWhenPrimaryPathIsUnavailable()
    {
        string directoryPath = CreateTemporaryDirectory();
        string blockingPath = Path.Combine(directoryPath, "not-a-directory");
        string filePath = Path.Combine(blockingPath, "documents.json");
        string recoveryFilePath = Path.Combine(directoryPath, "documents-recovery.json");
        KustoDocumentWorkspace workspace = new(
            [new KustoDocument(Guid.NewGuid(), "Recovered", "print 1", 0, null, null)],
            null);

        try
        {
            await File.WriteAllTextAsync(blockingPath, string.Empty);
            FileKustoDocumentStore store = new(filePath, recoveryFilePath);

            IOException exception = await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(workspace));

            Assert.Contains(recoveryFilePath, exception.Message, StringComparison.Ordinal);
            KustoDocument recovered = Assert.Single(new FileKustoDocumentStore(recoveryFilePath).Load().Documents);
            Assert.Equal("Recovered", recovered.Title);
            Assert.Equal("print 1", recovered.Text);
        }
        finally
        {
            Directory.Delete(directoryPath, true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string directoryPath = Path.Combine(Path.GetTempPath(), $"OpenKustoExplorer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        return directoryPath;
    }
}
