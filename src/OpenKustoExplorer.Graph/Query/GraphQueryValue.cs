using System.Globalization;

namespace OpenKustoExplorer.Graph.Query;

/// <summary>
/// Carries one Native AOT-safe scalar or graph identity result value.
/// </summary>
public sealed class GraphQueryValue
{
    private GraphQueryValue(
        GraphQueryValueKind kind,
        string displayText,
        GraphEntityKey? entity,
        GraphRelationshipKey? relationship)
    {
        Kind = kind;
        DisplayText = displayText;
        Entity = entity;
        Relationship = relationship;
    }

    /// <summary>
    /// Gets the value kind.
    /// </summary>
    public GraphQueryValueKind Kind { get; }

    /// <summary>
    /// Gets the invariant display text.
    /// </summary>
    public string DisplayText { get; }

    /// <summary>
    /// Gets the typed entity identity when <see cref="Kind"/> is <see cref="GraphQueryValueKind.Entity"/>.
    /// </summary>
    public GraphEntityKey? Entity { get; }

    /// <summary>
    /// Gets the typed relationship identity when <see cref="Kind"/> is
    /// <see cref="GraphQueryValueKind.Relationship"/>.
    /// </summary>
    public GraphRelationshipKey? Relationship { get; }

    /// <summary>
    /// Creates a Cypher null value.
    /// </summary>
    /// <returns>A Cypher null value.</returns>
    public static GraphQueryValue FromNull() => new(GraphQueryValueKind.Null, string.Empty, null, null);

    /// <summary>
    /// Creates a text value.
    /// </summary>
    /// <param name="value">The text value.</param>
    /// <returns>A graph query text value.</returns>
    public static GraphQueryValue FromString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new GraphQueryValue(GraphQueryValueKind.Text, value, null, null);
    }

    /// <summary>
    /// Creates a signed integer value.
    /// </summary>
    /// <param name="value">The signed integer value.</param>
    /// <returns>A graph query whole-number value.</returns>
    public static GraphQueryValue FromInteger(long value)
    {
        return new GraphQueryValue(
            GraphQueryValueKind.WholeNumber,
            value.ToString(CultureInfo.InvariantCulture),
            null,
            null);
    }

    /// <summary>
    /// Creates a floating-point value.
    /// </summary>
    /// <param name="value">The finite floating-point value.</param>
    /// <returns>A graph query floating-point value.</returns>
    public static GraphQueryValue FromFloatingPoint(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Graph query numbers must be finite.");
        }

        return new GraphQueryValue(
            GraphQueryValueKind.FloatingPoint,
            value.ToString("R", CultureInfo.InvariantCulture),
            null,
            null);
    }

    /// <summary>
    /// Creates a Boolean value.
    /// </summary>
    /// <param name="value">The Boolean value.</param>
    /// <returns>A graph query Boolean value.</returns>
    public static GraphQueryValue FromBoolean(bool value)
    {
        return new GraphQueryValue(
            GraphQueryValueKind.Boolean,
            value ? "true" : "false",
            null,
            null);
    }

    /// <summary>
    /// Creates a typed entity identity value.
    /// </summary>
    /// <param name="entity">The graph entity identity.</param>
    /// <returns>A typed graph entity value.</returns>
    public static GraphQueryValue FromEntity(GraphEntityKey entity)
    {
        return new GraphQueryValue(GraphQueryValueKind.Entity, entity.ToString(), entity, null);
    }

    /// <summary>
    /// Creates a typed relationship identity value.
    /// </summary>
    /// <param name="relationship">The graph relationship identity.</param>
    /// <returns>A typed graph relationship value.</returns>
    public static GraphQueryValue FromRelationship(GraphRelationshipKey relationship)
    {
        return new GraphQueryValue(
            GraphQueryValueKind.Relationship,
            relationship.ToString(),
            null,
            relationship);
    }
}
