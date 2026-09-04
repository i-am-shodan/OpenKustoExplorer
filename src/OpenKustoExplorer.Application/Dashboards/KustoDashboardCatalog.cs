namespace OpenKustoExplorer.Application.Dashboards;

/// <summary>
/// Contains every persisted dashboard in display order.
/// </summary>
public sealed class KustoDashboardCatalog
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoDashboardCatalog"/> class.
    /// </summary>
    /// <param name="dashboards">The persisted dashboards.</param>
    public KustoDashboardCatalog(IEnumerable<KustoDashboard> dashboards)
    {
        ArgumentNullException.ThrowIfNull(dashboards);
        KustoDashboard[] dashboardArray = dashboards.ToArray();

        if (dashboardArray.Select(dashboard => dashboard.Id).Distinct().Count() != dashboardArray.Length)
        {
            throw new ArgumentException("Dashboard identifiers must be unique.", nameof(dashboards));
        }

        Dashboards = Array.AsReadOnly(dashboardArray);
    }

    /// <summary>
    /// Gets persisted dashboards in display order.
    /// </summary>
    public IReadOnlyList<KustoDashboard> Dashboards { get; }
}
