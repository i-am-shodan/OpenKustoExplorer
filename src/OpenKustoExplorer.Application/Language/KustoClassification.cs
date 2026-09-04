namespace OpenKustoExplorer.Application.Language;

/// <summary>
/// Describes a classified range of KQL source text.
/// </summary>
public sealed class KustoClassification
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoClassification"/> class.
    /// </summary>
    /// <param name="kind">The stable classification name supplied by the Kusto language service.</param>
    /// <param name="start">The zero-based source offset.</param>
    /// <param name="length">The number of source characters in the range.</param>
    public KustoClassification(string kind, int start, int length)
    {
        Kind = kind;
        Start = start;
        Length = length;
    }

    /// <summary>
    /// Gets the stable classification name supplied by the Kusto language service.
    /// </summary>
    public string Kind { get; }

    /// <summary>
    /// Gets the zero-based source offset.
    /// </summary>
    public int Start { get; }

    /// <summary>
    /// Gets the number of source characters in the range.
    /// </summary>
    public int Length { get; }
}
