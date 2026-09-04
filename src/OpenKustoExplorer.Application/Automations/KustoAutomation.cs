namespace OpenKustoExplorer.Application.Automations;

/// <summary>
/// Describes one persisted scheduled KQL query and its bounded run history.
/// </summary>
public sealed class KustoAutomation
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoAutomation"/> class.
    /// </summary>
    /// <param name="id">The stable automation identifier.</param>
    /// <param name="name">The user-defined automation name.</param>
    /// <param name="clusterUri">The query cluster URI.</param>
    /// <param name="databaseName">The query database.</param>
    /// <param name="queryText">The independent KQL query block.</param>
    /// <param name="interval">The recurrence interval.</param>
    /// <param name="createdAtUtc">The UTC creation time.</param>
    /// <param name="nextRunAtUtc">The next planned UTC execution time.</param>
    /// <param name="stopAtUtc">The optional UTC schedule end.</param>
    /// <param name="isEnabled">Whether future executions are enabled.</param>
    /// <param name="runs">The completed run history from oldest to newest.</param>
    /// <param name="notificationSettings">Optional notification criteria, channels, and templates.</param>
    public KustoAutomation(
        Guid id,
        string name,
        Uri clusterUri,
        string databaseName,
        string queryText,
        TimeSpan interval,
        DateTimeOffset createdAtUtc,
        DateTimeOffset nextRunAtUtc,
        DateTimeOffset? stopAtUtc,
        bool isEnabled,
        IEnumerable<KustoAutomationRun> runs,
        KustoAutomationNotificationSettings? notificationSettings = null)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(clusterUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(queryText);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(runs);

        if (!clusterUri.IsAbsoluteUri || clusterUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("An automation cluster URI must be absolute HTTPS.", nameof(clusterUri));
        }

        if (stopAtUtc is not null && stopAtUtc <= createdAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(stopAtUtc), "The stop time must follow creation.");
        }

        Id = id;
        Name = name.Trim();
        ClusterUri = clusterUri;
        DatabaseName = databaseName.Trim();
        QueryText = queryText.Trim();
        Interval = interval;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        NextRunAtUtc = nextRunAtUtc.ToUniversalTime();
        StopAtUtc = stopAtUtc?.ToUniversalTime();
        IsEnabled = isEnabled;
        Runs = Array.AsReadOnly(runs.OrderBy(run => run.StartedAtUtc).ToArray());
        NotificationSettings = notificationSettings ?? KustoAutomationNotificationSettings.Disabled;
    }

    /// <summary>
    /// Gets the stable automation identifier.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Gets the user-defined automation name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the query cluster URI.
    /// </summary>
    public Uri ClusterUri { get; }

    /// <summary>
    /// Gets the query database.
    /// </summary>
    public string DatabaseName { get; }

    /// <summary>
    /// Gets the independent KQL query block.
    /// </summary>
    public string QueryText { get; }

    /// <summary>
    /// Gets the recurrence interval.
    /// </summary>
    public TimeSpan Interval { get; }

    /// <summary>
    /// Gets the UTC creation time.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>
    /// Gets the next planned UTC execution time.
    /// </summary>
    public DateTimeOffset NextRunAtUtc { get; }

    /// <summary>
    /// Gets the optional UTC schedule end.
    /// </summary>
    public DateTimeOffset? StopAtUtc { get; }

    /// <summary>
    /// Gets a value indicating whether future executions are enabled.
    /// </summary>
    public bool IsEnabled { get; }

    /// <summary>
    /// Gets completed run history from oldest to newest.
    /// </summary>
    public IReadOnlyList<KustoAutomationRun> Runs { get; }

    /// <summary>
    /// Gets notification criteria, channels, and message templates.
    /// </summary>
    public KustoAutomationNotificationSettings NotificationSettings { get; }
}
