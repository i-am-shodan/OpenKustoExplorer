using System.Net;
using System.Net.Sockets;
using OpenKustoExplorer.Kusto.Execution;

namespace OpenKustoExplorer.Web.Kusto;

/// <summary>
/// Restricts Web-hosted Kusto calls to public Azure Data Explorer query endpoints.
/// </summary>
public sealed class PublicAdxEndpointPolicy : IKustoEndpointPolicy
{
    private const string PublicAdxHostSuffix = ".kusto.windows.net";
    private readonly IHostAddressResolver resolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="PublicAdxEndpointPolicy"/> class.
    /// </summary>
    /// <param name="resolver">The resolver used to reject nonpublic destinations.</param>
    public PublicAdxEndpointPolicy(IHostAddressResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        this.resolver = resolver;
    }

    /// <inheritdoc />
    public async ValueTask<Uri> ValidateAsync(
        Uri clusterUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clusterUri);

        bool hasValidAuthority = clusterUri.IsAbsoluteUri
            && clusterUri.Scheme == Uri.UriSchemeHttps
            && clusterUri.IsDefaultPort
            && string.IsNullOrEmpty(clusterUri.UserInfo)
            && string.IsNullOrEmpty(clusterUri.Query)
            && string.IsNullOrEmpty(clusterUri.Fragment)
            && (string.IsNullOrEmpty(clusterUri.AbsolutePath) || clusterUri.AbsolutePath == "/");
        string host = clusterUri.IdnHost.TrimEnd('.');
        bool hasValidHost = host.EndsWith(PublicAdxHostSuffix, StringComparison.OrdinalIgnoreCase)
            && host.Length > PublicAdxHostSuffix.Length
            && !host.StartsWith("ingest-", StringComparison.OrdinalIgnoreCase)
            && !IPAddress.TryParse(host, out _);

        if (!hasValidAuthority || !hasValidHost)
        {
            throw new ArgumentException(
                "Only public Azure Data Explorer HTTPS cluster URLs are supported.",
                nameof(clusterUri));
        }

        IPAddress[] addresses = await resolver
            .GetHostAddressesAsync(host, cancellationToken)
            .ConfigureAwait(false);
        if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address)))
        {
            throw new InvalidOperationException(
                "The Azure Data Explorer cluster did not resolve exclusively to public addresses.");
        }

        return new Uri($"https://{host}", UriKind.Absolute);
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.None)
            || address.Equals(IPAddress.IPv6None)
            || address.IsIPv6LinkLocal
            || address.IsIPv6Multicast
            || address.IsIPv6SiteLocal)
        {
            return false;
        }

        byte[] bytes = address.GetAddressBytes();
        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsPublicIpv4(bytes),
            AddressFamily.InterNetworkV6 => IsPublicIpv6(bytes),
            _ => false,
        };
    }

    private static bool IsPublicIpv4(byte[] bytes)
    {
        return bytes[0] != 0
            && bytes[0] != 10
            && bytes[0] != 127
            && !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
            && !(bytes[0] == 169 && bytes[1] == 254)
            && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            && !(bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0)
            && !(bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2)
            && !(bytes[0] == 192 && bytes[1] == 168)
            && !(bytes[0] == 198 && bytes[1] is 18 or 19)
            && !(bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100)
            && !(bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113)
            && bytes[0] < 224;
    }

    private static bool IsPublicIpv6(byte[] bytes)
    {
        bool isUniqueLocal = (bytes[0] & 0xfe) == 0xfc;
        bool isDocumentation = bytes[0] == 0x20
            && bytes[1] == 0x01
            && bytes[2] == 0x0d
            && bytes[3] == 0xb8;
        return !isUniqueLocal && !isDocumentation;
    }
}
