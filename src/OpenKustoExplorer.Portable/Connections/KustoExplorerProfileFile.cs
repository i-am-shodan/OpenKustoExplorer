namespace OpenKustoExplorer.Portable.Connections;

/// <summary>
/// Contains one explicitly selected Microsoft Kusto Explorer profile file.
/// </summary>
public sealed class KustoExplorerProfileFile
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoExplorerProfileFile"/> class.
    /// </summary>
    /// <param name="relativePath">The source-relative path used to resolve connection groups.</param>
    /// <param name="content">The complete text file content.</param>
    public KustoExplorerProfileFile(string relativePath, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(content);
        RelativePath = string.Join(
            '/',
            relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries));
        Content = content;
    }

    /// <summary>Gets the complete text file content.</summary>
    public string Content { get; }

    /// <summary>Gets the normalized source-relative path.</summary>
    public string RelativePath { get; }
}
