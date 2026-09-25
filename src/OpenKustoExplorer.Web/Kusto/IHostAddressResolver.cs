using System.Net;

namespace OpenKustoExplorer.Web.Kusto;

/// <summary>
/// Resolves a host name for outbound destination validation.
/// </summary>
public interface IHostAddressResolver
{
    /// <summary>
    /// Resolves every address currently advertised for a host.
    /// </summary>
    /// <param name="host">The DNS host name.</param>
    /// <param name="cancellationToken">A token that cancels resolution.</param>
    /// <returns>The resolved addresses.</returns>
    public ValueTask<IPAddress[]> GetHostAddressesAsync(
        string host,
        CancellationToken cancellationToken = default);
}
