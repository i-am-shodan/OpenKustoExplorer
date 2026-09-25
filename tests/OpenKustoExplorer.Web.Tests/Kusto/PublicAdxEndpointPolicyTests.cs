using System.Net;
using OpenKustoExplorer.Web.Kusto;

namespace OpenKustoExplorer.Web.Tests.Kusto;

/// <summary>
/// Verifies the Web host's public Azure Data Explorer destination policy.
/// </summary>
public sealed class PublicAdxEndpointPolicyTests
{
    /// <summary>
    /// Verifies a standard public Azure Data Explorer host is canonicalized.
    /// </summary>
    /// <returns>A task that completes after destination validation.</returns>
    [Fact]
    public async Task ValidateAsyncAcceptsPublicAdxCluster()
    {
        PublicAdxEndpointPolicy policy = new(new StubResolver(IPAddress.Parse("20.42.1.10")));

        Uri result = await policy.ValidateAsync(new Uri("https://Help.Kusto.Windows.Net/"));

        Assert.Equal(new Uri("https://help.kusto.windows.net"), result);
    }

    /// <summary>
    /// Verifies malformed and non-ADX authorities are rejected before DNS resolution.
    /// </summary>
    /// <param name="clusterUri">The rejected destination.</param>
    /// <returns>A task that completes after destination validation.</returns>
    [Theory]
    [InlineData("http://help.kusto.windows.net")]
    [InlineData("https://help.kusto.windows.net:444")]
    [InlineData("https://user@help.kusto.windows.net")]
    [InlineData("https://help.kusto.windows.net/query")]
    [InlineData("https://help.kusto.windows.net.evil.example")]
    [InlineData("https://ingest-help.kusto.windows.net")]
    public async Task ValidateAsyncRejectsInvalidAuthority(string clusterUri)
    {
        PublicAdxEndpointPolicy policy = new(new StubResolver(IPAddress.Parse("20.42.1.10")));

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await policy.ValidateAsync(new Uri(clusterUri)));
    }

    /// <summary>
    /// Verifies an allowed-looking host cannot resolve to a private destination.
    /// </summary>
    /// <param name="address">The rejected resolved address.</param>
    /// <returns>A task that completes after destination validation.</returns>
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.5")]
    [InlineData("169.254.169.254")]
    [InlineData("192.168.1.1")]
    [InlineData("::1")]
    [InlineData("fc00::1")]
    [InlineData("fe80::1")]
    public async Task ValidateAsyncRejectsNonpublicResolution(string address)
    {
        PublicAdxEndpointPolicy policy = new(new StubResolver(IPAddress.Parse(address)));

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await policy.ValidateAsync(new Uri("https://help.kusto.windows.net")));

        Assert.Contains("public addresses", exception.Message, StringComparison.Ordinal);
    }

    private sealed class StubResolver : IHostAddressResolver
    {
        private readonly IPAddress[] addresses;

        internal StubResolver(params IPAddress[] addresses)
        {
            this.addresses = addresses;
        }

        public ValueTask<IPAddress[]> GetHostAddressesAsync(
            string host,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(addresses);
        }
    }
}
