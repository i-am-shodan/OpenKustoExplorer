using OpenKustoExplorer.Application.Execution;

namespace OpenKustoExplorer.Application.Dashboards;

/// <summary>
/// Describes one persisted KQL dashboard widget.
/// </summary>
public sealed class KustoDashboardWidget
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoDashboardWidget"/> class.
    /// </summary>
    /// <param name="id">The stable widget identifier.</param>
    /// <param name="title">The widget title.</param>
    /// <param name="clusterUri">The query cluster URI.</param>
    /// <param name="databaseName">The query database.</param>
    /// <param name="queryText">The widget KQL query.</param>
    /// <param name="refreshInterval">The automatic refresh interval.</param>
    /// <param name="displayMode">How results are presented.</param>
    /// <param name="visualizationKind">The visualization kind, or table for table widgets.</param>
    /// <param name="layout">The snapped canvas placement.</param>
    /// <param name="backgroundColor">The widget background color.</param>
    /// <param name="foregroundColor">The widget text color.</param>
    /// <param name="accentColor">The widget accent color.</param>
    /// <param name="cachedResult">The optional last successful materialized result.</param>
    /// <param name="cachedAtUtc">The optional UTC time at which the cached result was refreshed.</param>
    public KustoDashboardWidget(
        Guid id,
        string title,
        Uri clusterUri,
        string databaseName,
        string queryText,
        TimeSpan refreshInterval,
        KustoDashboardWidgetDisplayMode displayMode,
        KustoVisualizationKind visualizationKind,
        KustoDashboardWidgetLayout layout,
        string backgroundColor,
        string foregroundColor,
        string accentColor,
        KustoQueryResult? cachedResult = null,
        DateTimeOffset? cachedAtUtc = null)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(clusterUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(queryText);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(refreshInterval, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(layout);
        if ((cachedResult is null) != (cachedAtUtc is null))
        {
            string parameterName = cachedResult is null ? nameof(cachedResult) : nameof(cachedAtUtc);
            throw new ArgumentException(
                "A dashboard result and its refresh time must be provided together.",
                parameterName);
        }

        if (!clusterUri.IsAbsoluteUri || clusterUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("A dashboard widget cluster URI must be absolute HTTPS.", nameof(clusterUri));
        }

        if (!Enum.IsDefined(displayMode))
        {
            throw new ArgumentOutOfRangeException(nameof(displayMode));
        }

        if (!Enum.IsDefined(visualizationKind)
            || (displayMode == KustoDashboardWidgetDisplayMode.Table
                && visualizationKind != KustoVisualizationKind.Table)
            || (displayMode == KustoDashboardWidgetDisplayMode.Visualization
                && visualizationKind == KustoVisualizationKind.Table))
        {
            throw new ArgumentOutOfRangeException(nameof(visualizationKind));
        }

        Id = id;
        Title = title.Trim();
        ClusterUri = clusterUri;
        DatabaseName = databaseName.Trim();
        QueryText = queryText.Trim();
        RefreshInterval = refreshInterval;
        DisplayMode = displayMode;
        VisualizationKind = visualizationKind;
        Layout = layout;
        BackgroundColor = KustoDashboardColor.Validate(backgroundColor, nameof(backgroundColor));
        ForegroundColor = KustoDashboardColor.Validate(foregroundColor, nameof(foregroundColor));
        AccentColor = KustoDashboardColor.Validate(accentColor, nameof(accentColor));
        CachedResult = cachedResult;
        CachedAtUtc = cachedAtUtc?.ToUniversalTime();
    }

    /// <summary>
    /// Gets the stable widget identifier.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Gets the widget title.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets the query cluster URI.
    /// </summary>
    public Uri ClusterUri { get; }

    /// <summary>
    /// Gets the query database.
    /// </summary>
    public string DatabaseName { get; }

    /// <summary>
    /// Gets the widget KQL query.
    /// </summary>
    public string QueryText { get; }

    /// <summary>
    /// Gets the automatic refresh interval.
    /// </summary>
    public TimeSpan RefreshInterval { get; }

    /// <summary>
    /// Gets how query results are presented.
    /// </summary>
    public KustoDashboardWidgetDisplayMode DisplayMode { get; }

    /// <summary>
    /// Gets the visualization kind, or table for table widgets.
    /// </summary>
    public KustoVisualizationKind VisualizationKind { get; }

    /// <summary>
    /// Gets the snapped canvas placement.
    /// </summary>
    public KustoDashboardWidgetLayout Layout { get; }

    /// <summary>
    /// Gets the widget background color.
    /// </summary>
    public string BackgroundColor { get; }

    /// <summary>
    /// Gets the widget text color.
    /// </summary>
    public string ForegroundColor { get; }

    /// <summary>
    /// Gets the widget accent color.
    /// </summary>
    public string AccentColor { get; }

    /// <summary>Gets the optional last successful materialized result.</summary>
    public KustoQueryResult? CachedResult { get; }

    /// <summary>Gets the optional UTC time at which the cached result was refreshed.</summary>
    public DateTimeOffset? CachedAtUtc { get; }
}
