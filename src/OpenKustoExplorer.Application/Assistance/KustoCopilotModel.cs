namespace OpenKustoExplorer.Application.Assistance;

/// <summary>
/// Describes one language model available to the signed-in GitHub Copilot account.
/// </summary>
public sealed class KustoCopilotModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoCopilotModel"/> class.
    /// </summary>
    /// <param name="id">The SDK model identifier.</param>
    /// <param name="name">The human-readable model name.</param>
    public KustoCopilotModel(string id, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = id.Trim();
        Name = name.Trim();
    }

    /// <summary>
    /// Gets the SDK model identifier.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the human-readable model name.
    /// </summary>
    public string Name { get; }
}
