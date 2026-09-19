namespace OpenKustoExplorer.Kusto.Execution;

/// <summary>
/// Validates and canonicalizes Kusto cluster destinations before authentication or network access.
/// </summary>
public interface IKustoEndpointPolicy
{
    /// <summary>
    /// Validates a requested cluster and returns its canonical authority URI.
    /// </summary>
    /// <param name="clusterUri">The requested cluster URI.</param>
    /// <param name="cancellationToken">A token that cancels destination validation.</param>
    /// <returns>The validated canonical cluster authority.</returns>
    public ValueTask<Uri> ValidateAsync(
        Uri clusterUri,
        CancellationToken cancellationToken = default);
}
