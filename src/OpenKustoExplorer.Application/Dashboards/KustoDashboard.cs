namespace OpenKustoExplorer.Application.Dashboards;

/// <summary>
/// Describes one persisted dashboard and its widgets.
/// </summary>
public sealed class KustoDashboard
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoDashboard"/> class.
    /// </summary>
    /// <param name="id">The stable dashboard identifier.</param>
    /// <param name="title">The dashboard title.</param>
    /// <param name="backgroundColor">The dashboard canvas color.</param>
    /// <param name="widgets">The dashboard widgets.</param>
    public KustoDashboard(
        Guid id,
        string title,
        string backgroundColor,
        IEnumerable<KustoDashboardWidget> widgets)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(widgets);
        KustoDashboardWidget[] widgetArray = widgets.ToArray();

        if (widgetArray.Select(widget => widget.Id).Distinct().Count() != widgetArray.Length)
        {
            throw new ArgumentException("Dashboard widget identifiers must be unique.", nameof(widgets));
        }

        Id = id;
        Title = title.Trim();
        BackgroundColor = KustoDashboardColor.Validate(backgroundColor, nameof(backgroundColor));
        Widgets = Array.AsReadOnly(widgetArray);
    }

    /// <summary>
    /// Gets the stable dashboard identifier.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Gets the dashboard title.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets the dashboard canvas color.
    /// </summary>
    public string BackgroundColor { get; }

    /// <summary>
    /// Gets dashboard widgets in display order.
    /// </summary>
    public IReadOnlyList<KustoDashboardWidget> Widgets { get; }
}
