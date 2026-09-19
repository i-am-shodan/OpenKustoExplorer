using System.Net;

namespace OpenKustoExplorer.Web.Kusto;

/// <summary>
/// Resolves host names through the operating system DNS configuration.
/// </summary>
internal sealed class SystemHostAddressResolver : IHostAddressResolver
{
    /// <inheritdoc />
    public async ValueTask<IPAddress[]> GetHostAddressesAsync(
        string host,
        CancellationToken cancellationToken = default)
    {
        return await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
    }
}
