namespace OpenKustoExplorer.Domain.Schema;

/// <summary>
/// Identifies a scalar type supported by Kusto table columns.
/// </summary>
public enum KustoScalarType
{
    /// <summary>
    /// A Boolean value.
    /// </summary>
    Bool,

    /// <summary>
    /// A coordinated universal time instant.
    /// </summary>
    DateTime,

    /// <summary>
    /// A high-precision fixed-point number.
    /// </summary>
    FixedPoint,

    /// <summary>
    /// A JSON-like dynamic value.
    /// </summary>
    Dynamic,

    /// <summary>
    /// A globally unique identifier.
    /// </summary>
    Identifier,

    /// <summary>
    /// A 32-bit signed integer.
    /// </summary>
    WholeNumber,

    /// <summary>
    /// A 64-bit signed integer.
    /// </summary>
    WideInteger,

    /// <summary>
    /// A double-precision floating-point number.
    /// </summary>
    Real,

    /// <summary>
    /// A Unicode text value.
    /// </summary>
    Text,

    /// <summary>
    /// A duration.
    /// </summary>
    TimeSpan,
}
