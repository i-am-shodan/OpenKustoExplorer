namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents one result-column filter operation.
/// </summary>
public sealed class KustoResultFilterOption
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoResultFilterOption"/> class.
    /// </summary>
    /// <param name="filterOperator">The operation applied to cell values.</param>
    /// <param name="displayName">The concise operation label.</param>
    /// <param name="requiresValue">Whether the operation needs comparison text.</param>
    internal KustoResultFilterOption(
        KustoResultFilterOperator filterOperator,
        string displayName,
        bool requiresValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        Operator = filterOperator;
        DisplayName = displayName;
        RequiresValue = requiresValue;
    }

    /// <summary>
    /// Gets the operation applied to cell values.
    /// </summary>
    public KustoResultFilterOperator Operator { get; }

    /// <summary>
    /// Gets the concise operation label.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets a value indicating whether the operation needs comparison text.
    /// </summary>
    public bool RequiresValue { get; }
}
