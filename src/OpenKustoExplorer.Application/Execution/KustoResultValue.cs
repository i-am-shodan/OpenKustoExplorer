namespace OpenKustoExplorer.Application.Execution;

/// <summary>
/// Preserves one Kusto result value for display and exact typed processing.
/// </summary>
public sealed class KustoResultValue
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoResultValue"/> class.
    /// </summary>
    /// <param name="displayText">The invariant display text.</param>
    /// <param name="rawJson">The optional exact JSON token returned by Kusto.</param>
    /// <param name="isNull">Whether the returned JSON token was null.</param>
    public KustoResultValue(string displayText, string? rawJson, bool isNull)
    {
        ArgumentNullException.ThrowIfNull(displayText);

        if (isNull && rawJson is not null && !string.Equals(rawJson, "null", StringComparison.Ordinal))
        {
            throw new ArgumentException("A null result value must use the JSON null token.", nameof(rawJson));
        }

        DisplayText = displayText;
        RawJson = rawJson;
        IsNull = isNull;
    }

    /// <summary>
    /// Gets the invariant display text.
    /// </summary>
    public string DisplayText { get; }

    /// <summary>
    /// Gets the exact JSON token returned by Kusto, or <see langword="null"/> for legacy display-only values.
    /// </summary>
    public string? RawJson { get; }

    /// <summary>
    /// Gets a value indicating whether the returned value was null.
    /// </summary>
    public bool IsNull { get; }
}
