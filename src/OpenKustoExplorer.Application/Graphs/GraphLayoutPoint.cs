namespace OpenKustoExplorer.Application.Graphs;

/// <summary>
/// Identifies one point in graph layout coordinates.
/// </summary>
public readonly struct GraphLayoutPoint : IEquatable<GraphLayoutPoint>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphLayoutPoint"/> struct.
    /// </summary>
    /// <param name="x">The horizontal layout coordinate.</param>
    /// <param name="y">The vertical layout coordinate.</param>
    public GraphLayoutPoint(double x, double y)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(x, x);
        ArgumentOutOfRangeException.ThrowIfNotEqual(y, y);

        if (double.IsInfinity(x))
        {
            throw new ArgumentOutOfRangeException(nameof(x), "A layout coordinate must be finite.");
        }

        if (double.IsInfinity(y))
        {
            throw new ArgumentOutOfRangeException(nameof(y), "A layout coordinate must be finite.");
        }

        X = x;
        Y = y;
    }

    /// <summary>
    /// Gets the horizontal layout coordinate.
    /// </summary>
    public double X { get; }

    /// <summary>
    /// Gets the vertical layout coordinate.
    /// </summary>
    public double Y { get; }

    /// <summary>
    /// Determines whether two points are equal.
    /// </summary>
    /// <param name="left">The first point.</param>
    /// <param name="right">The second point.</param>
    /// <returns><see langword="true"/> when both coordinates are equal.</returns>
    public static bool operator ==(GraphLayoutPoint left, GraphLayoutPoint right) => left.Equals(right);

    /// <summary>
    /// Determines whether two points differ.
    /// </summary>
    /// <param name="left">The first point.</param>
    /// <param name="right">The second point.</param>
    /// <returns><see langword="true"/> when either coordinate differs.</returns>
    public static bool operator !=(GraphLayoutPoint left, GraphLayoutPoint right) => !left.Equals(right);

    /// <inheritdoc />
    public bool Equals(GraphLayoutPoint other) =>
        EqualityComparer<double>.Default.Equals(X, other.X)
        && EqualityComparer<double>.Default.Equals(Y, other.Y);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is GraphLayoutPoint other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(X, Y);
}
