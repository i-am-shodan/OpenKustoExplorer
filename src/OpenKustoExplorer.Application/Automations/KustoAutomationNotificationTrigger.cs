namespace OpenKustoExplorer.Application.Automations;

/// <summary>
/// Describes one matched automation notification criterion.
/// </summary>
public sealed class KustoAutomationNotificationTrigger
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoAutomationNotificationTrigger"/> class.
    /// </summary>
    /// <param name="kind">The matched criterion kind.</param>
    /// <param name="comparison">The optional fixed row-count comparison.</param>
    /// <param name="comparisonValue">The optional fixed comparison value.</param>
    internal KustoAutomationNotificationTrigger(
        KustoAutomationNotificationTriggerKind kind,
        KustoAutomationRowCountComparison? comparison = null,
        int? comparisonValue = null)
    {
        Kind = kind;
        Comparison = comparison;
        ComparisonValue = comparisonValue;
    }

    /// <summary>
    /// Gets the matched criterion kind.
    /// </summary>
    public KustoAutomationNotificationTriggerKind Kind { get; }

    /// <summary>
    /// Gets the fixed row-count comparison when <see cref="Kind"/> is
    /// <see cref="KustoAutomationNotificationTriggerKind.RowCountComparison"/>.
    /// </summary>
    public KustoAutomationRowCountComparison? Comparison { get; }

    /// <summary>
    /// Gets the fixed comparison value when <see cref="Kind"/> is
    /// <see cref="KustoAutomationNotificationTriggerKind.RowCountComparison"/>.
    /// </summary>
    public int? ComparisonValue { get; }
}
