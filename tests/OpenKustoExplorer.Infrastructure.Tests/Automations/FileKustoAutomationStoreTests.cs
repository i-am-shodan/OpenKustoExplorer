using OpenKustoExplorer.Application.Automations;
using OpenKustoExplorer.Application.Execution;
using OpenKustoExplorer.Infrastructure.Automations;

namespace OpenKustoExplorer.Infrastructure.Tests.Automations;

/// <summary>
/// Verifies durable scheduled-query definitions and result history.
/// </summary>
public sealed class FileKustoAutomationStoreTests
{
    /// <summary>
    /// Verifies schedules, successful visualized results, and failures round-trip through JSON.
    /// </summary>
    [Fact]
    public void SaveAndLoadRoundTripsAutomationHistory()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "automations.json");
        DateTimeOffset createdAt = new(2026, 7, 22, 10, 0, 0, TimeSpan.Zero);
        KustoResultTable table = new(
            "PrimaryResult",
            [new KustoResultColumn("Timestamp", "datetime"), new KustoResultColumn("Events", "long")],
            [new KustoResultRow(["2026-07-22T10:00:00Z", "42"])]);
        KustoVisualization visualization = new(
            KustoVisualizationKind.TimeChart,
            title: "Events over time",
            xColumn: "Timestamp",
            yColumns: ["Events"]);
        KustoQueryResult result = new([table], TimeSpan.FromMilliseconds(125), visualization);
        KustoAutomationRun succeededRun = new(
            Guid.NewGuid(),
            createdAt.AddMinutes(5),
            createdAt.AddMinutes(5).AddMilliseconds(125),
            KustoAutomationRunStatus.Succeeded,
            null,
            result,
            new Uri("https://original-adx.contoso.com"),
            "HistoricalTelemetry");
        KustoAutomationRun failedRun = new(
            Guid.NewGuid(),
            createdAt.AddMinutes(10),
            createdAt.AddMinutes(10).AddSeconds(1),
            KustoAutomationRunStatus.Failed,
            "Authentication failed",
            null);
        Guid automationId = Guid.NewGuid();
        KustoAutomationWebhookSettings webhook = new(
            KustoAutomationWebhookEndpointSource.StoredUrl,
            new Uri("https://hooks.example.com/automation"));
        KustoAutomationNotificationSettings notifications = new(
            true,
            KustoAutomationRowCountComparison.GreaterThanOrEqual,
            10,
            true,
            true,
            "operator@example.com",
            "monitor@example.com",
            "smtp.example.com",
            587,
            true,
            "{name}: {row_count}",
            "Changed by {rows_changed}\n{query}",
            true,
            "C:\\Tools\\handle-result.exe",
            "--rows {row_count} --name \"{name}\"",
            webhook);
        KustoAutomation automation = new(
            automationId,
            "Traffic monitor",
            new Uri("https://adx.contoso.com"),
            "Telemetry",
            "Events | summarize Events=count() by bin(Timestamp, 1h) | render timechart",
            TimeSpan.FromMinutes(5),
            createdAt,
            createdAt.AddMinutes(15),
            createdAt.AddDays(1),
            true,
            [succeededRun, failedRun],
            notifications);

        try
        {
            FileKustoAutomationStore store = new(filePath);
            store.Save(new KustoAutomationCatalog([automation]));

            KustoAutomation restored = Assert.Single(store.Load().Automations);

            Assert.Equal(automationId, restored.Id);
            Assert.Equal("Traffic monitor", restored.Name);
            Assert.Equal(TimeSpan.FromMinutes(5), restored.Interval);
            Assert.Equal(createdAt.AddDays(1), restored.StopAtUtc);
            Assert.Equal(2, restored.Runs.Count);
            KustoQueryResult restoredResult = Assert.IsType<KustoQueryResult>(restored.Runs[0].Result);
            Assert.Equal("42", Assert.Single(Assert.Single(restoredResult.Tables).Rows).Values[1]);
            Assert.Equal(KustoVisualizationKind.TimeChart, restoredResult.Visualization?.Kind);
            Assert.Equal("Events over time", restoredResult.Visualization?.Title);
            Assert.Equal("Authentication failed", restored.Runs[1].ErrorMessage);
            Assert.Equal("original-adx.contoso.com", restored.Runs[0].ClusterUri?.Host);
            Assert.Equal("HistoricalTelemetry", restored.Runs[0].DatabaseName);
            Assert.Equal("adx.contoso.com", restored.Runs[1].ClusterUri?.Host);
            Assert.Equal("Telemetry", restored.Runs[1].DatabaseName);
            Assert.True(restored.NotificationSettings.NotifyWhenRowCountChanges);
            Assert.Equal(
                KustoAutomationRowCountComparison.GreaterThanOrEqual,
                restored.NotificationSettings.RowCountComparison);
            Assert.Equal("operator@example.com", restored.NotificationSettings.EmailRecipient);
            Assert.Equal("Changed by {rows_changed}\n{query}", restored.NotificationSettings.MessageTemplate);
            Assert.True(restored.NotificationSettings.RunApplicationEnabled);
            Assert.Equal("C:\\Tools\\handle-result.exe", restored.NotificationSettings.ApplicationPath);
            Assert.Equal(
                "--rows {row_count} --name \"{name}\"",
                restored.NotificationSettings.ApplicationArguments);
            Assert.Equal(
                KustoAutomationWebhookEndpointSource.StoredUrl,
                restored.NotificationSettings.Webhook?.EndpointSource);
            Assert.Equal(
                "https://hooks.example.com/automation",
                restored.NotificationSettings.Webhook?.StoredUrl?.AbsoluteUri.TrimEnd('/'));
        }
        finally
        {
            Directory.Delete(directoryPath, true);
        }
    }

    /// <summary>
    /// Verifies version-one catalogs written before notification support load with notifications disabled.
    /// </summary>
    [Fact]
    public void LoadAcceptsLegacyCatalogWithoutNotifications()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "automations.json");
        string json = $$"""
                        {
                            "version": 1,
                            "automations": [
                                {
                                    "id": "{{Guid.NewGuid():D}}",
                                    "name": "Legacy",
                                    "clusterUri": "https://adx.example.com/",
                                    "databaseName": "Telemetry",
                                    "queryText": "Events | count",
                                    "intervalSeconds": 300,
                                    "createdAtUtc": "2026-07-23T10:00:00+00:00",
                                    "nextRunAtUtc": "2026-07-23T10:05:00+00:00",
                                    "stopAtUtc": null,
                                    "isEnabled": true,
                                    "runs": [
                                        {
                                            "id": "{{Guid.NewGuid():D}}",
                                            "startedAtUtc": "2026-07-23T10:00:00+00:00",
                                            "completedAtUtc": "2026-07-23T10:00:01+00:00",
                                            "status": "Failed",
                                            "errorMessage": "Legacy failure"
                                        }
                                    ]
                                }
                            ]
                        }
                        """;

        try
        {
            File.WriteAllText(filePath, json);
            FileKustoAutomationStore store = new(filePath);

            KustoAutomation restored = Assert.Single(store.Load().Automations);

            Assert.False(restored.NotificationSettings.HasEnabledChannel);
            Assert.Equal(
                KustoAutomationRowCountComparison.None,
                restored.NotificationSettings.RowCountComparison);
            KustoAutomationRun legacyRun = Assert.Single(restored.Runs);
            Assert.Equal("adx.example.com", legacyRun.ClusterUri?.Host);
            Assert.Equal("Telemetry", legacyRun.DatabaseName);
        }
        finally
        {
            Directory.Delete(directoryPath, true);
        }
    }

    /// <summary>
    /// Verifies version-two catalogs load with the webhook channel disabled.
    /// </summary>
    [Fact]
    public void LoadAcceptsVersionTwoWithoutWebhook()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "automations.json");
        DateTimeOffset utcNow = new(2026, 9, 16, 10, 0, 0, TimeSpan.Zero);
        KustoAutomation automation = new(
            Guid.NewGuid(),
            "Version two",
            new Uri("https://adx.example.com"),
            "Telemetry",
            "Events | count",
            TimeSpan.FromMinutes(5),
            utcNow,
            utcNow.AddMinutes(5),
            null,
            true,
            []);

        try
        {
            FileKustoAutomationStore store = new(filePath);
            store.Save(new KustoAutomationCatalog([automation]));
            string versionTwoJson = File.ReadAllText(filePath)
                .Replace("\"version\": 3", "\"version\": 2", StringComparison.Ordinal);
            File.WriteAllText(filePath, versionTwoJson);

            KustoAutomation restored = Assert.Single(store.Load().Automations);

            Assert.Null(restored.NotificationSettings.Webhook);
            Assert.False(restored.NotificationSettings.HasEnabledChannel);
        }
        finally
        {
            Directory.Delete(directoryPath, true);
        }
    }

    /// <summary>
    /// Verifies corrupt automation state cannot prevent application startup.
    /// </summary>
    [Fact]
    public void LoadReturnsEmptyCatalogForCorruptJson()
    {
        string directoryPath = CreateTemporaryDirectory();
        string filePath = Path.Combine(directoryPath, "automations.json");

        try
        {
            File.WriteAllText(filePath, "not json");
            FileKustoAutomationStore store = new(filePath);

            Assert.Empty(store.Load().Automations);
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
