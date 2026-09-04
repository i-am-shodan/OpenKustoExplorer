namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Identifies source text associated with the latest query execution failure.
/// </summary>
public sealed class KustoQueryErrorHighlight
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoQueryErrorHighlight"/> class.
    /// </summary>
    /// <param name="start">The zero-based document start offset.</param>
    /// <param name="length">The number of document characters to highlight.</param>
    /// <param name="lineNumber">The one-based document line number.</param>
    /// <param name="columnNumber">The optional one-based service column number.</param>
    public KustoQueryErrorHighlight(int start, int length, int lineNumber, int? columnNumber)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lineNumber);

        if (columnNumber is not null)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columnNumber.Value);
        }

        Start = start;
        Length = length;
        LineNumber = lineNumber;
        ColumnNumber = columnNumber;
    }

    /// <summary>
    /// Gets the zero-based document start offset.
    /// </summary>
    public int Start { get; }

    /// <summary>
    /// Gets the number of document characters to highlight.
    /// </summary>
    public int Length { get; }

    /// <summary>
    /// Gets the one-based document line number.
    /// </summary>
    public int LineNumber { get; }

    /// <summary>
    /// Gets the optional one-based service column number.
    /// </summary>
    public int? ColumnNumber { get; }

    /// <summary>
    /// Gets concise source location text.
    /// </summary>
    public string LocationText => ColumnNumber is int column
        ? $"Line {LineNumber:N0}, column {column:N0}"
        : $"Statement starting at line {LineNumber:N0}";
}
