namespace OpenKustoExplorer.Application.Connections;

/// <summary>
/// Identifies one accessible database discovered on an Azure Data Explorer cluster.
/// </summary>
public sealed class KustoDatabaseInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoDatabaseInfo"/> class.
    /// </summary>
    /// <param name="name">The case-sensitive database name.</param>
    /// <param name="displayName">The preferred database display name.</param>
    public KustoDatabaseInfo(string name, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        Name = name;
        DisplayName = displayName;
    }

    /// <summary>
    /// Gets the case-sensitive database name used by Kusto requests.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the preferred database display name.
    /// </summary>
    public string DisplayName { get; }
}
