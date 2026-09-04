using OpenKustoExplorer.Domain.Schema;

namespace OpenKustoExplorer.Application.Connections;

/// <summary>
/// Describes one persisted database and its optional cached schema.
/// </summary>
public sealed class KustoDatabaseConnection
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoDatabaseConnection"/> class.
    /// </summary>
    /// <param name="name">The case-sensitive database name.</param>
    /// <param name="displayName">The preferred database display name.</param>
    /// <param name="schema">The cached schema, or <see langword="null"/> when it has not been loaded.</param>
    public KustoDatabaseConnection(string name, string displayName, KustoDatabaseSchema? schema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        Name = name;
        DisplayName = displayName;
        Schema = schema;
    }

    /// <summary>
    /// Gets the case-sensitive database name used by Kusto requests.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the preferred database display name.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets the cached schema, or <see langword="null"/> when it has not been loaded.
    /// </summary>
    public KustoDatabaseSchema? Schema { get; }
}
