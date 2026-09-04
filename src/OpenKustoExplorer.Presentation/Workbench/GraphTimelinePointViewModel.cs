using System.Globalization;
using OpenKustoExplorer.Graph;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents either the live graph or one retained investigation timeline point.
/// </summary>
public sealed class GraphTimelinePointViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphTimelinePointViewModel"/> class.
    /// </summary>
    /// <param name="point">The retained timeline point, or <see langword="null"/> for the live graph.</param>
    public GraphTimelinePointViewModel(GraphTimelinePoint? point)
    {
        Point = point;

        if (point is null)
        {
            Title = "Live graph";
            Detail = "Current active generation";
        }
        else
        {
            Title = point.TimestampUtc.ToLocalTime().ToString(
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.CurrentCulture);
            Detail = point.IsGenerationStart
                ? point.SourceName
                : $"{point.SourceName} · {point.SourceKind}";
        }
    }

    /// <summary>
    /// Gets the complete accessible timeline label.
    /// </summary>
    public string AutomationName => $"{Title}, {Detail}";

    /// <summary>
    /// Gets the secondary source description.
    /// </summary>
    public string Detail { get; }

    /// <summary>
    /// Gets a value indicating whether this item selects the live graph.
    /// </summary>
    public bool IsLive => Point is null;

    /// <summary>
    /// Gets the retained point, or <see langword="null"/> for the live graph.
    /// </summary>
    public GraphTimelinePoint? Point { get; }

    /// <summary>
    /// Gets the local timestamp or live-graph label.
    /// </summary>
    public string Title { get; }
}
