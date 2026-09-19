namespace OpenKustoExplorer.Kusto.Execution;

/// <summary>
/// Accepts absolute HTTPS Kusto destinations and canonicalizes them to their authority.
/// </summary>
public sealed class HttpsKustoEndpointPolicy : IKustoEndpointPolicy
{
    /// <inheritdoc />
    public ValueTask<Uri> ValidateAsync(
        Uri clusterUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clusterUri);
        cancellationToken.ThrowIfCancellationRequested();

        if (!clusterUri.IsAbsoluteUri
            || clusterUri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(clusterUri.Host))
        {
            throw new ArgumentException(
                "The cluster URI must be an absolute HTTPS URI.",
                nameof(clusterUri));
        }

        return ValueTask.FromResult(new Uri(
            clusterUri.GetLeftPart(UriPartial.Authority),
            UriKind.Absolute));
    }
}
