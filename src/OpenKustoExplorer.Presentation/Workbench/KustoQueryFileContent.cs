namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Contains one KQL file selected for import into a query tab.
/// </summary>
public sealed class KustoQueryFileContent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoQueryFileContent"/> class.
    /// </summary>
    /// <param name="fileName">The source file name.</param>
    /// <param name="text">The decoded KQL text.</param>
    public KustoQueryFileContent(string fileName, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(text);
        FileName = fileName;
        Text = text;
    }

    /// <summary>Gets the source file name.</summary>
    public string FileName { get; }

    /// <summary>Gets the decoded KQL text.</summary>
    public string Text { get; }
}
