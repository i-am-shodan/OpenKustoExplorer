using OpenKustoExplorer.Application.Graphs;

namespace OpenKustoExplorer.Desktop.Graphs;

/// <summary>
/// Describes one cubic Bezier segment in graph layout coordinates.
/// </summary>
internal readonly struct KustoGraphCurveSegment
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoGraphCurveSegment"/> struct.
    /// </summary>
    /// <param name="start">The segment start.</param>
    /// <param name="firstControl">The first control point.</param>
    /// <param name="secondControl">The second control point.</param>
    /// <param name="end">The segment end.</param>
    internal KustoGraphCurveSegment(
        GraphLayoutPoint start,
        GraphLayoutPoint firstControl,
        GraphLayoutPoint secondControl,
        GraphLayoutPoint end)
    {
        Start = start;
        FirstControl = firstControl;
        SecondControl = secondControl;
        End = end;
    }

    /// <summary>
    /// Gets the segment end.
    /// </summary>
    internal GraphLayoutPoint End { get; }

    /// <summary>
    /// Gets the first control point.
    /// </summary>
    internal GraphLayoutPoint FirstControl { get; }

    /// <summary>
    /// Gets the second control point.
    /// </summary>
    internal GraphLayoutPoint SecondControl { get; }

    /// <summary>
    /// Gets the segment start.
    /// </summary>
    internal GraphLayoutPoint Start { get; }
}
