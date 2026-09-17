using OpenKustoExplorer.Application.Automations;
using OpenKustoExplorer.Application.Execution;

namespace OpenKustoExplorer.Application.Tests.Automations;

/// <summary>
/// Verifies automation notification criteria and helper substitution.
/// </summary>
public sealed class KustoAutomationNotificationTests
{
    /// <summary>
    /// Verifies every fixed row-count operator produces the expected match result.
    /// </summary>
    /// <param name="comparison">The comparison under test.</param>
    /// <param name="comparisonValue">The configured fixed value.</param>
    /// <param name="expectedMatch">Whether six current rows should match.</param>
    [Theory]
    [InlineData(KustoAutomationRowCountComparison.Equals, 6, true)]
    [InlineData(KustoAutomationRowCountComparison.Equals, 5, false)]
    [InlineData(KustoAutomationRowCountComparison.GreaterThanOrEqual, 6, true)]
    [InlineData(KustoAutomationRowCountComparison.GreaterThanOrEqual, 7, false)]
    [InlineData(KustoAutomationRowCountComparison.LessThanOrEqual, 6, true)]
    [InlineData(KustoAutomationRowCountComparison.LessThanOrEqual, 5, false)]
    [InlineData(KustoAutomationRowCountComparison.NotEquals, 5, true)]
    [InlineData(KustoAutomationRowCountComparison.NotEquals, 6, false)]
    public void FixedRowCountComparisonMatchesExpectedResult(
        KustoAutomationRowCountComparison comparison,
        int comparisonValue,
        bool expectedMatch)
    {
        DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;
        KustoAutomationNotificationSettings settings = new(
            false,
            comparison,
            comparisonValue,
            true,
            false,
            null,
            null,
            null,
            587,
            true,
            "{name}: {row_count}",
            "{rows_changed} | {query}");
        KustoAutomation automation = new(
            Guid.NewGuid(),
            "Monitor",
            new Uri("https://adx.example.com"),
            "Telemetry",
            "Events | take 6",
            TimeSpan.FromMinutes(5),
            startedAtUtc.AddHours(-1),
            startedAtUtc.AddMinutes(5),
            null,
            true,
            [],
            settings);
        KustoAutomationRun currentRun = CreateSuccessfulRun(startedAtUtc, 6);

        KustoAutomationNotification? notification = KustoAutomationNotification.TryCreate(
            automation,
            currentRun,
            CreateSuccessfulRun(startedAtUtc.AddMinutes(-5), 4));

        Assert.Equal(expectedMatch, notification is not null);
        if (notification is not null)
        {
            Assert.Equal("Monitor: 6", notification.Title);
            Assert.Equal("+2 | Events | take 6", notification.Message);
        }
    }

    /// <summary>
    /// Verifies an application-only action can trigger when a successful query returns no rows.
    /// </summary>
    [Fact]
    public void ApplicationActionMatchesZeroRowResultWithoutNotificationChannels()
    {
        DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;
        KustoAutomationNotificationSettings settings = new(
            false,
            KustoAutomationRowCountComparison.Equals,
            0,
            false,
            false,
            null,
            null,
            null,
            587,
            true,
            "{name}: {row_count}",
            "{query}",
            true,
            "C:\\Tools\\handle-empty-result.exe",
            "--rows {row_count} --name \"{name}\"");
        KustoAutomation automation = new(
            Guid.NewGuid(),
            "Empty result monitor",
            new Uri("https://adx.example.com"),
            "Telemetry",
            "Events | where false",
            TimeSpan.FromMinutes(5),
            startedAtUtc.AddHours(-1),
            startedAtUtc.AddMinutes(5),
            null,
            true,
            [],
            settings);

        KustoAutomationNotification? notification = KustoAutomationNotification.TryCreate(
            automation,
            CreateSuccessfulRun(startedAtUtc, 0),
            null);

        Assert.NotNull(notification);
        Assert.Equal(0, notification.RowCount);
        Assert.True(notification.Settings.RunApplicationEnabled);
        Assert.Equal("C:\\Tools\\handle-empty-result.exe", notification.Settings.ApplicationPath);
        Assert.Equal("--rows {row_count} --name \"{name}\"", notification.Settings.ApplicationArguments);
        Assert.Equal("--rows 0 --name \"Empty result monitor\"", notification.ApplicationArguments);
    }

    /// <summary>
    /// Verifies a webhook-only action snapshots metadata and every matched criterion.
    /// </summary>
    [Fact]
    public void WebhookOnlyActionSnapshotsMetadataAndOrderedTriggers()
    {
        DateTimeOffset startedAtUtc = new(2026, 9, 16, 10, 0, 0, TimeSpan.Zero);
        KustoAutomationWebhookSettings webhook = new(
            KustoAutomationWebhookEndpointSource.EnvironmentVariable,
            environmentVariableName: "OPENKUSTOEXPLORER_TEST_WEBHOOK");
        KustoAutomationNotificationSettings settings = new(
            true,
            KustoAutomationRowCountComparison.GreaterThanOrEqual,
            5,
            false,
            false,
            null,
            null,
            null,
            587,
            true,
            "{name}: {row_count}",
            "{query}",
            webhook: webhook);
        Guid automationId = Guid.NewGuid();
        KustoAutomation automation = new(
            automationId,
            "Monitor",
            new Uri("https://adx.example.com"),
            "Telemetry",
            "Events | take 6",
            TimeSpan.FromMinutes(5),
            startedAtUtc.AddHours(-1),
            startedAtUtc.AddMinutes(5),
            null,
            true,
            [],
            settings);
        KustoAutomationRun currentRun = CreateSuccessfulRun(startedAtUtc, 6);

        KustoAutomationNotification notification = Assert.IsType<KustoAutomationNotification>(
            KustoAutomationNotification.TryCreate(
                automation,
                currentRun,
                CreateSuccessfulRun(startedAtUtc.AddMinutes(-5), 4)));

        Assert.True(settings.HasEnabledChannel);
        Assert.Equal(automationId, notification.AutomationId);
        Assert.Equal("Monitor", notification.AutomationName);
        Assert.Equal(currentRun.Id, notification.RunId);
        Assert.Equal(startedAtUtc, notification.StartedAtUtc);
        Assert.Equal(currentRun.CompletedAtUtc, notification.CompletedAtUtc);
        Assert.Equal(KustoAutomationRunStatus.Succeeded, notification.RunStatus);
        Assert.Equal("adx.example.com", notification.ClusterUri.Host);
        Assert.Equal("Telemetry", notification.DatabaseName);
        Assert.Collection(
            notification.Triggers,
            trigger => Assert.Equal(
                KustoAutomationNotificationTriggerKind.RowCountChanged,
                trigger.Kind),
            trigger =>
            {
                Assert.Equal(KustoAutomationNotificationTriggerKind.RowCountComparison, trigger.Kind);
                Assert.Equal(KustoAutomationRowCountComparison.GreaterThanOrEqual, trigger.Comparison);
                Assert.Equal(5, trigger.ComparisonValue);
            });
    }

    /// <summary>
    /// Verifies stored endpoints require HTTPS and reject URL user information.
    /// </summary>
    /// <param name="url">The invalid URL.</param>
    [Theory]
    [InlineData("http://example.com/hook")]
    [InlineData("https://user@example.com/hook")]
    public void StoredWebhookRejectsUnsafeUrl(string url)
    {
        Assert.Throws<ArgumentException>(
            () => new KustoAutomationWebhookSettings(
                KustoAutomationWebhookEndpointSource.StoredUrl,
                new Uri(url)));
    }

    private static KustoAutomationRun CreateSuccessfulRun(DateTimeOffset startedAtUtc, int rowCount)
    {
        KustoResultTable table = new(
            "PrimaryResult",
            [new KustoResultColumn("Value", "long")],
            Enumerable.Range(0, rowCount).Select(index => new KustoResultRow([$"{index}"])));
        return new KustoAutomationRun(
            Guid.NewGuid(),
            startedAtUtc,
            startedAtUtc.AddMilliseconds(5),
            KustoAutomationRunStatus.Succeeded,
            null,
            new KustoQueryResult([table], TimeSpan.FromMilliseconds(5)));
    }
}
