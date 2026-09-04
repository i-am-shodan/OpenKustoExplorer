using CommunityToolkit.Mvvm.Input;
using OpenKustoExplorer.Application.Documents;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents one immutable conditional-formatting rule and its removal action.
/// </summary>
public sealed class KustoConditionalFormatRuleViewModel
{
    private readonly KustoConditionalFormatRule rule;

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoConditionalFormatRuleViewModel"/> class.
    /// </summary>
    /// <param name="rule">The immutable persisted rule.</param>
    /// <param name="removeAction">Removes this rule from its document.</param>
    public KustoConditionalFormatRuleViewModel(
        KustoConditionalFormatRule rule,
        Action<KustoConditionalFormatRuleViewModel> removeAction)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(removeAction);

        this.rule = rule;
        RemoveCommand = new RelayCommand(() => removeAction(this));
    }

    /// <summary>
    /// Gets the stable rule identifier.
    /// </summary>
    public Guid Id => rule.Id;

    /// <summary>
    /// Gets the result column evaluated by the rule.
    /// </summary>
    public string ColumnName => rule.ColumnName;

    /// <summary>
    /// Gets the comparison operator.
    /// </summary>
    public KustoConditionalFormatOperator Comparison => rule.Comparison;

    /// <summary>
    /// Gets the comparison value or percentile.
    /// </summary>
    public string ComparisonValue => rule.ComparisonValue;

    /// <summary>
    /// Gets the formatting target.
    /// </summary>
    public KustoConditionalFormatTarget Target => rule.Target;

    /// <summary>
    /// Gets the opaque RGB formatting color.
    /// </summary>
    public string ColorHex => rule.ColorHex;

    /// <summary>
    /// Gets the concise rule summary.
    /// </summary>
    public string Summary => $"{ColumnName} · {Comparison} · {ComparisonValue} · {Target}";

    /// <summary>
    /// Gets the command that removes this rule.
    /// </summary>
    public IRelayCommand RemoveCommand { get; }

    /// <summary>
    /// Creates the immutable persistence snapshot.
    /// </summary>
    /// <returns>The source rule.</returns>
    internal KustoConditionalFormatRule CreateRule()
    {
        return rule;
    }
}
