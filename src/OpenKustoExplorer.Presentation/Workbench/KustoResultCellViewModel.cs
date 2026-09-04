using CommunityToolkit.Mvvm.ComponentModel;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents one display value in a Kusto result table.
/// </summary>
public sealed class KustoResultCellViewModel : ObservableObject
{
    private string backgroundHex = "#00000000";

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoResultCellViewModel"/> class.
    /// </summary>
    /// <param name="row">The owning result row.</param>
    /// <param name="columnIndex">The zero-based result column index.</param>
    /// <param name="columnName">The result column name.</param>
    /// <param name="typeName">The server-reported column type.</param>
    /// <param name="text">The invariant display text.</param>
    /// <param name="displayWidth">The content-fitted column width.</param>
    public KustoResultCellViewModel(
        KustoResultRowViewModel row,
        int columnIndex,
        string columnName,
        string typeName,
        string text,
        double displayWidth)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentOutOfRangeException.ThrowIfNegative(columnIndex);
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(displayWidth);

        Row = row;
        ColumnIndex = columnIndex;
        ColumnName = columnName;
        TypeName = typeName;
        Text = text;
        DisplayWidth = displayWidth;
    }

    /// <summary>
    /// Gets the owning result row.
    /// </summary>
    public KustoResultRowViewModel Row { get; }

    /// <summary>
    /// Gets the zero-based result column index.
    /// </summary>
    public int ColumnIndex { get; }

    /// <summary>
    /// Gets the result column name.
    /// </summary>
    public string ColumnName { get; }

    /// <summary>
    /// Gets the server-reported column type.
    /// </summary>
    public string TypeName { get; }

    /// <summary>
    /// Gets the invariant display text.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the content-fitted width shared with this cell's column header.
    /// </summary>
    public double DisplayWidth { get; }

    /// <summary>
    /// Gets the conditional cell background color.
    /// </summary>
    public string BackgroundHex
    {
        get => backgroundHex;
        private set => SetProperty(ref backgroundHex, value);
    }

    /// <summary>
    /// Replaces the conditional cell background color.
    /// </summary>
    /// <param name="colorHex">The ARGB or RGB color text.</param>
    internal void SetBackground(string colorHex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(colorHex);
        BackgroundHex = colorHex;
    }
}
