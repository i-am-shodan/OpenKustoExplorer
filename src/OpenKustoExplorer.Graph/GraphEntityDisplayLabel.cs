namespace OpenKustoExplorer.Graph;

/// <summary>
/// Defines validation for analyst-facing graph entity labels.
/// </summary>
public static class GraphEntityDisplayLabel
{
    /// <summary>
    /// Gets the maximum supported entity label length.
    /// </summary>
    public const int MaximumLength = 200;

    /// <summary>
    /// Validates and normalizes an analyst-entered entity label.
    /// </summary>
    /// <param name="displayLabel">The entity label.</param>
    /// <returns>The trimmed label.</returns>
    public static string Normalize(string displayLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayLabel);
        string normalized = displayLabel.Trim();
        if (normalized.Length > MaximumLength)
        {
            throw new ArgumentOutOfRangeException(nameof(displayLabel));
        }

        return normalized;
    }
}
