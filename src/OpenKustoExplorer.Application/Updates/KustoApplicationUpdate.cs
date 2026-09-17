namespace OpenKustoExplorer.Application.Updates;

/// <summary>
/// Describes a newer official application release.
/// </summary>
public sealed class KustoApplicationUpdate
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoApplicationUpdate"/> class.
    /// </summary>
    /// <param name="version">The stable release version.</param>
    /// <param name="tagName">The official GitHub release tag.</param>
    /// <param name="releasePageUri">The official GitHub release page.</param>
    public KustoApplicationUpdate(Version version, string tagName, Uri releasePageUri)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(tagName);
        ArgumentNullException.ThrowIfNull(releasePageUri);

        if (!releasePageUri.IsAbsoluteUri
            || releasePageUri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(releasePageUri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(releasePageUri.UserInfo))
        {
            throw new ArgumentException(
                "The release page must be an official GitHub HTTPS URI.",
                nameof(releasePageUri));
        }

        Version = version;
        TagName = tagName.Trim();
        ReleasePageUri = releasePageUri;
    }

    /// <summary>
    /// Gets the stable release version.
    /// </summary>
    public Version Version { get; }

    /// <summary>
    /// Gets the official GitHub release tag.
    /// </summary>
    public string TagName { get; }

    /// <summary>
    /// Gets the official GitHub release page.
    /// </summary>
    public Uri ReleasePageUri { get; }
}
