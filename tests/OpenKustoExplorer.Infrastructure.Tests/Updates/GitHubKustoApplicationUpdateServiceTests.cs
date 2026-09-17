using System.Net;
using OpenKustoExplorer.Application.Updates;
using OpenKustoExplorer.Infrastructure.Updates;

namespace OpenKustoExplorer.Infrastructure.Tests.Updates;

/// <summary>
/// Verifies GitHub stable-release discovery and version comparison.
/// </summary>
public sealed class GitHubKustoApplicationUpdateServiceTests
{
    /// <summary>
    /// Verifies a newer official release is returned with the expected request headers.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckReturnsNewerStableRelease()
    {
        RecordingHttpMessageHandler handler = new(
            HttpStatusCode.OK,
            """{"tag_name":"v1.2.0"}""");
        using HttpClient client = new(handler);
        using GitHubKustoApplicationUpdateService service = new(client);

        KustoApplicationUpdate? update = await service.CheckForUpdateAsync(new Version(1, 1, 3, 0));

        KustoApplicationUpdate availableUpdate = Assert.IsType<KustoApplicationUpdate>(update);
        Assert.Equal(new Version(1, 2, 0), availableUpdate.Version);
        Assert.Equal("v1.2.0", availableUpdate.TagName);
        Assert.Equal(
            "https://github.com/i-am-shodan/OpenKustoExplorer/releases/latest",
            availableUpdate.ReleasePageUri.AbsoluteUri.TrimEnd('/'));
        Assert.Equal(
            "https://api.github.com/repos/i-am-shodan/OpenKustoExplorer/releases/latest",
            handler.RequestUri?.AbsoluteUri);
        Assert.Contains("OpenKustoExplorer/1.1.3.0", handler.UserAgent, StringComparison.Ordinal);
        Assert.Equal("application/vnd.github+json", handler.Accept);
        Assert.Equal("2022-11-28", handler.ApiVersion);
    }

    /// <summary>
    /// Verifies equivalent three- and four-part versions do not offer an update.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckIgnoresEquivalentRelease()
    {
        RecordingHttpMessageHandler handler = new(
            HttpStatusCode.OK,
            """{"tag_name":"v1.0.0"}""");
        using HttpClient client = new(handler);
        using GitHubKustoApplicationUpdateService service = new(client);

        KustoApplicationUpdate? update = await service.CheckForUpdateAsync(new Version(1, 0, 0, 0));

        Assert.Null(update);
    }

    /// <summary>
    /// Verifies prerelease tags are not treated as stable updates.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckRejectsPrereleaseTag()
    {
        RecordingHttpMessageHandler handler = new(
            HttpStatusCode.OK,
            """{"tag_name":"v2.0.0-beta.1"}""");
        using HttpClient client = new(handler);
        using GitHubKustoApplicationUpdateService service = new(client);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.CheckForUpdateAsync(new Version(1, 0, 0, 0)));
    }

    /// <summary>
    /// Verifies GitHub failures remain transport failures for the startup caller to ignore.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckPropagatesNonSuccessStatus()
    {
        RecordingHttpMessageHandler handler = new(HttpStatusCode.Forbidden, "{}");
        using HttpClient client = new(handler);
        using GitHubKustoApplicationUpdateService service = new(client);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.CheckForUpdateAsync(new Version(1, 0, 0, 0)));
    }

    /// <summary>
    /// Verifies update actions cannot be constructed with a non-GitHub destination.
    /// </summary>
    [Fact]
    public void UpdateRejectsNonGitHubReleasePage()
    {
        Assert.Throws<ArgumentException>(() => new KustoApplicationUpdate(
            new Version(2, 0, 0),
            "v2.0.0",
            new Uri("https://example.com/releases/v2.0.0")));
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly string responseBody;
        private readonly HttpStatusCode statusCode;

        public RecordingHttpMessageHandler(HttpStatusCode statusCode, string responseBody)
        {
            this.statusCode = statusCode;
            this.responseBody = responseBody;
        }

        public string? Accept { get; private set; }

        public string? ApiVersion { get; private set; }

        public Uri? RequestUri { get; private set; }

        public string UserAgent { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            UserAgent = request.Headers.UserAgent.ToString();
            Accept = request.Headers.Accept.Single().MediaType;
            ApiVersion = Assert.Single(request.Headers.GetValues("X-GitHub-Api-Version"));
            return Task.FromResult(CreateResponse());
        }

        private HttpResponseMessage CreateResponse()
        {
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody),
            };
        }
    }
}
