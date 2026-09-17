using System.Net.Http.Headers;
using System.Text.Json;
using OpenKustoExplorer.Application.Updates;

namespace OpenKustoExplorer.Infrastructure.Updates;

/// <summary>
/// Checks the official GitHub repository for its latest stable release.
/// </summary>
public sealed class GitHubKustoApplicationUpdateService : IKustoApplicationUpdateService, IDisposable
{
    private static readonly Uri LatestReleaseApiUri = new(
        "https://api.github.com/repos/i-am-shodan/OpenKustoExplorer/releases/latest");

    private static readonly Uri LatestReleasePageUri = new(
        "https://github.com/i-am-shodan/OpenKustoExplorer/releases/latest");

    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubKustoApplicationUpdateService"/> class.
    /// </summary>
    public GitHubKustoApplicationUpdateService()
        : this(CreateHttpClient(), ownsHttpClient: true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubKustoApplicationUpdateService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP transport.</param>
    /// <param name="ownsHttpClient">Whether disposal also disposes the HTTP transport.</param>
    internal GitHubKustoApplicationUpdateService(HttpClient httpClient, bool ownsHttpClient = false)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        this.httpClient = httpClient;
        this.ownsHttpClient = ownsHttpClient;
    }

    /// <inheritdoc />
    public async Task<KustoApplicationUpdate?> CheckForUpdateAsync(
        Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);
        using HttpRequestMessage request = new(HttpMethod.Get, LatestReleaseApiUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd($"OpenKustoExplorer/{NormalizeVersion(currentVersion)}");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using Stream content = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using JsonDocument document = await JsonDocument.ParseAsync(
            content,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        string tagName = ReadTagName(document.RootElement);
        Version latestVersion = ParseStableVersion(tagName);
        return NormalizeVersion(latestVersion) > NormalizeVersion(currentVersion)
            ? new KustoApplicationUpdate(latestVersion, tagName, LatestReleasePageUri)
            : null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
    }

    private static HttpClient CreateHttpClient()
    {
        return new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
    }

    private static Version NormalizeVersion(Version version)
    {
        return new Version(
            version.Major,
            version.Minor,
            Math.Max(0, version.Build),
            Math.Max(0, version.Revision));
    }

    private static Version ParseStableVersion(string tagName)
    {
        string versionText = tagName.Trim();
        if (versionText.StartsWith('v') || versionText.StartsWith('V'))
        {
            versionText = versionText[1..];
        }

        int metadataIndex = versionText.IndexOf('+', StringComparison.Ordinal);
        if (metadataIndex >= 0)
        {
            versionText = versionText[..metadataIndex];
        }

        if (versionText.Contains('-', StringComparison.Ordinal)
            || !Version.TryParse(versionText, out Version? version)
            || version.Build < 0)
        {
            throw new InvalidDataException($"GitHub returned an unsupported stable release tag '{tagName}'.");
        }

        return version;
    }

    private static string ReadTagName(JsonElement root)
    {
        if (!root.TryGetProperty("tag_name", out JsonElement tagElement)
            || tagElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(tagElement.GetString()))
        {
            throw new InvalidDataException("The GitHub release response does not contain a tag name.");
        }

        return tagElement.GetString()!;
    }
}
