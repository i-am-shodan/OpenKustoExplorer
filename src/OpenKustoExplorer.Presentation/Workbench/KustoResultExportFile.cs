namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Contains one generated result export and its suggested file metadata.
/// </summary>
public sealed class KustoResultExportFile
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoResultExportFile"/> class.
    /// </summary>
    /// <param name="suggestedFileName">The suggested file name.</param>
    /// <param name="contentType">The MIME content type.</param>
    /// <param name="content">The complete file bytes.</param>
    public KustoResultExportFile(string suggestedFileName, string contentType, byte[] content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestedFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentNullException.ThrowIfNull(content);

        SuggestedFileName = suggestedFileName;
        ContentType = contentType;
        Content = content;
    }

    /// <summary>
    /// Gets the suggested file name.
    /// </summary>
    public string SuggestedFileName { get; }

    /// <summary>
    /// Gets the MIME content type.
    /// </summary>
    public string ContentType { get; }

    /// <summary>
    /// Gets the complete file bytes.
    /// </summary>
    public byte[] Content { get; }
}
