using System.Globalization;

namespace OpenKustoExplorer.Application.Automations;

/// <summary>
/// Contains one evaluated and fully formatted automation notification.
/// </summary>
public sealed class KustoAutomationNotification
{
    private KustoAutomationNotification(
        KustoAutomationNotificationSettings settings,
        Guid automationId,
        string automationName,
        Guid runId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        KustoAutomationRunStatus runStatus,
        Uri clusterUri,
        string databaseName,
        IReadOnlyList<KustoAutomationNotificationTrigger> triggers,
        int rowCount,
        int rowsChanged,
        string title,
        string message,
        string? applicationArguments)
    {
        Settings = settings;
        AutomationId = automationId;
        AutomationName = automationName;
        RunId = runId;
        StartedAtUtc = startedAtUtc;
        CompletedAtUtc = completedAtUtc;
        RunStatus = runStatus;
        ClusterUri = clusterUri;
        DatabaseName = databaseName;
        Triggers = triggers;
        RowCount = rowCount;
        RowsChanged = rowsChanged;
        Title = title;
        Message = message;
        ApplicationArguments = applicationArguments;
    }

    /// <summary>
    /// Gets the channel and transport settings.
    /// </summary>
    public KustoAutomationNotificationSettings Settings { get; }

    /// <summary>
    /// Gets the stable automation identifier.
    /// </summary>
    public Guid AutomationId { get; }

    /// <summary>
    /// Gets the automation name at evaluation time.
    /// </summary>
    public string AutomationName { get; }

    /// <summary>
    /// Gets the stable run identifier used as the webhook event identifier.
    /// </summary>
    public Guid RunId { get; }

    /// <summary>
    /// Gets the run's UTC start time.
    /// </summary>
    public DateTimeOffset StartedAtUtc { get; }

    /// <summary>
    /// Gets the run's UTC completion time.
    /// </summary>
    public DateTimeOffset CompletedAtUtc { get; }

    /// <summary>
    /// Gets the run status at evaluation time.
    /// </summary>
    public KustoAutomationRunStatus RunStatus { get; }

    /// <summary>
    /// Gets the cluster targeted by the evaluated run.
    /// </summary>
    public Uri ClusterUri { get; }

    /// <summary>
    /// Gets the database targeted by the evaluated run.
    /// </summary>
    public string DatabaseName { get; }

    /// <summary>
    /// Gets matched notification criteria in deterministic order.
    /// </summary>
    public IReadOnlyList<KustoAutomationNotificationTrigger> Triggers { get; }

    /// <summary>
    /// Gets the current materialized row count.
    /// </summary>
    public int RowCount { get; }

    /// <summary>
    /// Gets the signed change from the previous successful run, or zero when none exists.
    /// </summary>
    public int RowsChanged { get; }

    /// <summary>
    /// Gets the formatted notification title.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets the formatted notification body.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the application arguments after result helper substitution.
    /// </summary>
    public string? ApplicationArguments { get; }

    /// <summary>
    /// Evaluates notification criteria for a successful automation run.
    /// </summary>
    /// <param name="automation">The immutable automation definition.</param>
    /// <param name="currentRun">The newly completed run.</param>
    /// <param name="previousSuccessfulRun">The preceding successful run, when available.</param>
    /// <returns>A formatted notification when a configured criterion matches; otherwise, <see langword="null"/>.</returns>
    public static KustoAutomationNotification? TryCreate(
        KustoAutomation automation,
        KustoAutomationRun currentRun,
        KustoAutomationRun? previousSuccessfulRun)
    {
        ArgumentNullException.ThrowIfNull(automation);
        ArgumentNullException.ThrowIfNull(currentRun);

        KustoAutomationNotificationSettings settings = automation.NotificationSettings;
        if (!settings.HasEnabledChannel
            || currentRun.Status != KustoAutomationRunStatus.Succeeded
            || currentRun.Result is null)
        {
            return null;
        }

        int rowCount = currentRun.Result.Tables.Sum(table => table.Rows.Count);
        int? previousRowCount = previousSuccessfulRun?.Result?.Tables.Sum(table => table.Rows.Count);
        int rowsChanged = previousRowCount is int previous ? rowCount - previous : 0;
        bool changedMatches = settings.NotifyWhenRowCountChanges
            && previousRowCount is not null
            && rowsChanged != 0;
        bool comparisonMatches = MatchesComparison(
            rowCount,
            settings.RowCountComparison,
            settings.RowCountValue);

        if (!changedMatches && !comparisonMatches)
        {
            return null;
        }

        string title = FormatTemplate(
            settings.SubjectTemplate,
            automation,
            rowCount,
            rowsChanged);
        string message = FormatTemplate(
            settings.MessageTemplate,
            automation,
            rowCount,
            rowsChanged);
        string? applicationArguments = settings.ApplicationArguments is null
            ? null
            : FormatTemplate(
                settings.ApplicationArguments,
                automation,
                rowCount,
                rowsChanged,
                useGroupedNumbers: false);
        List<KustoAutomationNotificationTrigger> triggers = [];
        if (changedMatches)
        {
            triggers.Add(new KustoAutomationNotificationTrigger(
                KustoAutomationNotificationTriggerKind.RowCountChanged));
        }

        if (comparisonMatches)
        {
            triggers.Add(new KustoAutomationNotificationTrigger(
                KustoAutomationNotificationTriggerKind.RowCountComparison,
                settings.RowCountComparison,
                settings.RowCountValue));
        }

        return new KustoAutomationNotification(
            settings,
            automation.Id,
            automation.Name,
            currentRun.Id,
            currentRun.StartedAtUtc,
            currentRun.CompletedAtUtc,
            currentRun.Status,
            currentRun.ClusterUri ?? automation.ClusterUri,
            currentRun.DatabaseName ?? automation.DatabaseName,
            Array.AsReadOnly(triggers.ToArray()),
            rowCount,
            rowsChanged,
            title,
            message,
            applicationArguments);
    }

    private static string FormatTemplate(
        string template,
        KustoAutomation automation,
        int rowCount,
        int rowsChanged,
        bool useGroupedNumbers = true)
    {
        string rowCountText = rowCount.ToString(
            useGroupedNumbers ? "N0" : "0",
            CultureInfo.InvariantCulture);
        string rowsChangedText = rowsChanged.ToString(
            useGroupedNumbers ? "+#;-#;0" : "+0;-0;0",
            CultureInfo.InvariantCulture);
        return template
            .Replace("{row_count}", rowCountText, StringComparison.OrdinalIgnoreCase)
            .Replace("{name}", automation.Name, StringComparison.OrdinalIgnoreCase)
            .Replace("{query}", automation.QueryText, StringComparison.OrdinalIgnoreCase)
            .Replace("{rows_changed}", rowsChangedText, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesComparison(
        int rowCount,
        KustoAutomationRowCountComparison comparison,
        int value)
    {
        return comparison switch
        {
            KustoAutomationRowCountComparison.Equals => rowCount == value,
            KustoAutomationRowCountComparison.GreaterThanOrEqual => rowCount >= value,
            KustoAutomationRowCountComparison.LessThanOrEqual => rowCount <= value,
            KustoAutomationRowCountComparison.NotEquals => rowCount != value,
            _ => false,
        };
    }
}
