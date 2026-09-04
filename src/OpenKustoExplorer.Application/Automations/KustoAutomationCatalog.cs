namespace OpenKustoExplorer.Application.Automations;

/// <summary>
/// Contains every persisted automation in display order.
/// </summary>
public sealed class KustoAutomationCatalog
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoAutomationCatalog"/> class.
    /// </summary>
    /// <param name="automations">The persisted automations.</param>
    public KustoAutomationCatalog(IEnumerable<KustoAutomation> automations)
    {
        ArgumentNullException.ThrowIfNull(automations);
        Automations = Array.AsReadOnly(automations.ToArray());
    }

    /// <summary>
    /// Gets persisted automations in display order.
    /// </summary>
    public IReadOnlyList<KustoAutomation> Automations { get; }
}
