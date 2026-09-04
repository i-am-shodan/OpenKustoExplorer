namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Identifies a supported result export representation.
/// </summary>
public enum KustoResultExportFormat
{
    /// <summary>
    /// Comma-separated UTF-8 text.
    /// </summary>
    Csv,

    /// <summary>
    /// Office Open XML workbook.
    /// </summary>
    Excel,

    /// <summary>
    /// UTF-8 JSON array.
    /// </summary>
    Json,
}
