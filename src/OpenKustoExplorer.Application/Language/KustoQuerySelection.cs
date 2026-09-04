namespace OpenKustoExplorer.Application.Language;

/// <summary>
/// Identifies the independent KQL query block selected by a caret position.
/// </summary>
public sealed class KustoQuerySelection
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoQuerySelection"/> class.
    /// </summary>
    /// <param name="text">The selected executable KQL text.</param>
    /// <param name="start">The zero-based document start offset.</param>
    /// <param name="length">The selected document length.</param>
    public KustoQuerySelection(string text, int start, int length)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);

        Text = text;
        Start = start;
        Length = length;
    }

    /// <summary>
    /// Gets the selected executable KQL text.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the zero-based document start offset.
    /// </summary>
    public int Start { get; }

    /// <summary>
    /// Gets the selected document length.
    /// </summary>
    public int Length { get; }
}
