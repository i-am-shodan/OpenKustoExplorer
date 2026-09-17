using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenKustoExplorer.Application.Dashboards;
using OpenKustoExplorer.Application.Execution;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents one durable dashboard and its runtime widgets.
/// </summary>
public sealed class KustoDashboardViewModel : ObservableObject, IDisposable
{
    private readonly Action<KustoDashboardViewModel> definitionChanged;
    private readonly IKustoQueryService queryService;
    private string backgroundColor;
    private bool isDisposed;
    private KustoDashboardTimeRange timeRange;
    private string title;

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoDashboardViewModel"/> class.
    /// </summary>
    /// <param name="definition">The persisted dashboard definition.</param>
    /// <param name="queryService">The Kusto query service.</param>
    /// <param name="definitionChanged">Persists dashboard definition changes.</param>
    public KustoDashboardViewModel(
        KustoDashboard definition,
        IKustoQueryService queryService,
        Action<KustoDashboardViewModel> definitionChanged)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(queryService);
        ArgumentNullException.ThrowIfNull(definitionChanged);
        this.queryService = queryService;
        this.definitionChanged = definitionChanged;
        Id = definition.Id;
        title = definition.Title;
        backgroundColor = definition.BackgroundColor;
        timeRange = definition.TimeRange;
        Widgets = new ObservableCollection<KustoDashboardWidgetViewModel>(
            definition.Widgets.Select(CreateWidgetViewModel));
    }

    /// <summary>
    /// Gets the stable dashboard identifier.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Gets the dashboard title.
    /// </summary>
    public string Title => title;

    /// <summary>
    /// Gets the dashboard canvas color.
    /// </summary>
    public string BackgroundColor => backgroundColor;

    /// <summary>
    /// Gets the dashboard-wide time range.
    /// </summary>
    public KustoDashboardTimeRange TimeRange => timeRange;

    /// <summary>
    /// Gets the concise active time-range label.
    /// </summary>
    public string TimeRangeSummary => TimeRange.Kind == KustoDashboardTimeRangeKind.Relative
        ? FormatRelativeTimeRange(TimeRange.RelativeDuration!.Value)
        : $"{TimeRange.StartUtc!.Value.ToLocalTime():g} - {TimeRange.EndUtc!.Value.ToLocalTime():g}";

    /// <summary>
    /// Gets a value indicating whether the active time range uses custom fixed bounds.
    /// </summary>
    public bool HasCustomTimeRange => TimeRange.Kind == KustoDashboardTimeRangeKind.Absolute;

    /// <summary>
    /// Gets query-backed widgets in display order.
    /// </summary>
    public ObservableCollection<KustoDashboardWidgetViewModel> Widgets { get; }

    /// <summary>
    /// Gets a value indicating whether this dashboard contains widgets.
    /// </summary>
    public bool HasWidgets => Widgets.Count > 0;

    /// <summary>
    /// Gets the concise widget-count text.
    /// </summary>
    public string WidgetCountText => Widgets.Count == 1 ? "1 widget" : $"{Widgets.Count:N0} widgets";

    /// <summary>
    /// Adds and persists a widget.
    /// </summary>
    /// <param name="definition">The widget definition.</param>
    /// <returns>The new runtime widget.</returns>
    public KustoDashboardWidgetViewModel AddWidget(KustoDashboardWidget definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (Widgets.Any(item => item.Id == definition.Id))
        {
            throw new ArgumentException("A dashboard widget identifier must be unique.", nameof(definition));
        }

        KustoDashboardWidgetViewModel widget = CreateWidgetViewModel(definition);
        Widgets.Add(widget);
        OnPropertyChanged(nameof(HasWidgets));
        OnPropertyChanged(nameof(WidgetCountText));
        definitionChanged(this);
        return widget;
    }

    /// <summary>
    /// Removes and disposes a widget.
    /// </summary>
    /// <param name="widget">The widget to remove.</param>
    public void RemoveWidget(KustoDashboardWidgetViewModel widget)
    {
        ArgumentNullException.ThrowIfNull(widget);

        if (Widgets.Remove(widget))
        {
            widget.Dispose();
            OnPropertyChanged(nameof(HasWidgets));
            OnPropertyChanged(nameof(WidgetCountText));
            definitionChanged(this);
        }
    }

    /// <summary>
    /// Replaces the dashboard title and canvas color.
    /// </summary>
    /// <param name="newTitle">The dashboard title.</param>
    /// <param name="newBackgroundColor">The dashboard canvas color.</param>
    public void ApplyDetails(string newTitle, string newBackgroundColor)
    {
        KustoDashboard validatedDefinition = new(Id, newTitle, newBackgroundColor, [], TimeRange);
        title = validatedDefinition.Title;
        backgroundColor = validatedDefinition.BackgroundColor;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(BackgroundColor));
        definitionChanged(this);
    }

    /// <summary>
    /// Replaces the dashboard-wide time range and invalidates widget results.
    /// </summary>
    /// <param name="newTimeRange">The validated replacement range.</param>
    /// <returns><see langword="true"/> when the definition changed; otherwise, <see langword="false"/>.</returns>
    public bool ApplyTimeRange(KustoDashboardTimeRange newTimeRange)
    {
        ArgumentNullException.ThrowIfNull(newTimeRange);

        if (HasSameTimeRange(TimeRange, newTimeRange))
        {
            return false;
        }

        timeRange = newTimeRange;
        foreach (KustoDashboardWidgetViewModel widget in Widgets)
        {
            widget.InvalidateForTimeRangeChange();
        }

        OnPropertyChanged(nameof(TimeRange));
        OnPropertyChanged(nameof(TimeRangeSummary));
        OnPropertyChanged(nameof(HasCustomTimeRange));
        definitionChanged(this);
        return true;
    }

    /// <summary>
    /// Creates the immutable persistence snapshot.
    /// </summary>
    /// <returns>The current dashboard definition.</returns>
    public KustoDashboard CreateDefinition()
    {
        return new KustoDashboard(
            Id,
            Title,
            BackgroundColor,
            Widgets.Select(item => item.CreateDefinition()),
            TimeRange);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!isDisposed)
        {
            isDisposed = true;

            foreach (KustoDashboardWidgetViewModel widget in Widgets)
            {
                widget.Dispose();
            }
        }
    }

    private static string FormatRelativeTimeRange(TimeSpan duration)
    {
        if (duration.Ticks >= TimeSpan.TicksPerDay
            && duration.Ticks % TimeSpan.TicksPerDay == 0)
        {
            long days = duration.Ticks / TimeSpan.TicksPerDay;
            return $"Last {days:N0} {(days == 1 ? "day" : "days")}";
        }

        if (duration.Ticks >= TimeSpan.TicksPerHour
            && duration.Ticks % TimeSpan.TicksPerHour == 0)
        {
            long hours = duration.Ticks / TimeSpan.TicksPerHour;
            return $"Last {hours:N0} {(hours == 1 ? "hour" : "hours")}";
        }

        long minutes = duration.Ticks / TimeSpan.TicksPerMinute;
        return $"Last {minutes:N0} minutes";
    }

    private static bool HasSameTimeRange(
        KustoDashboardTimeRange left,
        KustoDashboardTimeRange right)
    {
        return left.Kind == right.Kind
            && left.RelativeDuration == right.RelativeDuration
            && left.StartUtc == right.StartUtc
            && left.EndUtc == right.EndUtc;
    }

    private KustoDashboardWidgetViewModel CreateWidgetViewModel(KustoDashboardWidget definition)
    {
        return new KustoDashboardWidgetViewModel(
            definition,
            queryService,
            _ => definitionChanged(this),
            utcNow => TimeRange.Resolve(utcNow));
    }
}
