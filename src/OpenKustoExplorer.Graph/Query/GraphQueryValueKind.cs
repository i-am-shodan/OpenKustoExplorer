namespace OpenKustoExplorer.Graph.Query;

/// <summary>
/// Identifies the strongly typed value carried by a graph query result cell.
/// </summary>
public enum GraphQueryValueKind
{
    /// <summary>
    /// The value is Cypher null.
    /// </summary>
    Null,

    /// <summary>
    /// The value is text.
    /// </summary>
    Text,

    /// <summary>
    /// The value is a signed integer.
    /// </summary>
    WholeNumber,

    /// <summary>
    /// The value is a floating-point number.
    /// </summary>
    FloatingPoint,

    /// <summary>
    /// The value is a Boolean.
    /// </summary>
    Boolean,

    /// <summary>
    /// The value identifies a graph entity.
    /// </summary>
    Entity,

    /// <summary>
    /// The value identifies a graph relationship.
    /// </summary>
    Relationship,
}
