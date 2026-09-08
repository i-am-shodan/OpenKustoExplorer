using CommunityToolkit.Mvvm.ComponentModel;
using OpenKustoExplorer.Application.Execution;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents one display value in a Kusto result table.
/// </summary>
public sealed class KustoResultCellViewModel : ObservableObject
{
    private string backgroundHex = "#00000000";
    private bool isActionTarget;
    private bool isChainEnd;
    private bool isChainStart;
    private bool isRecordedInterestMatch;
    private bool isRecordedManualMatch;
    private bool isRecordedPertinent;
    private string recordingAccentHex = "#00000000";
    private string recordingHighlightHex = "#00000000";

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
        : this(
            row,
            columnIndex,
            columnName,
            typeName,
            new KustoResultValue(text, null, false),
            displayWidth)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoResultCellViewModel"/> class.
    /// </summary>
    /// <param name="row">The owning result row.</param>
    /// <param name="columnIndex">The zero-based result column index.</param>
    /// <param name="columnName">The result column name.</param>
    /// <param name="typeName">The server-reported column type.</param>
    /// <param name="value">The typed result value.</param>
    /// <param name="displayWidth">The content-fitted column width.</param>
    public KustoResultCellViewModel(
        KustoResultRowViewModel row,
        int columnIndex,
        string columnName,
        string typeName,
        KustoResultValue value,
        double displayWidth)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentOutOfRangeException.ThrowIfNegative(columnIndex);
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(displayWidth);

        Row = row;
        ColumnIndex = columnIndex;
        ColumnName = columnName;
        TypeName = typeName;
        Value = value;
        Text = value.DisplayText;
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
    /// Gets the row, column, and value text exposed to assistive technology.
    /// </summary>
    public string AutomationText => $"Row {Row.RowIndex + 1}, {ColumnName}: {AutomationValueText}";

    /// <summary>
    /// Gets the typed result value.
    /// </summary>
    public KustoResultValue Value { get; }

    /// <summary>
    /// Gets the content-fitted width shared with this cell's column header.
    /// </summary>
    public double DisplayWidth { get; }

    /// <summary>
    /// Gets a value indicating whether this cell is selected for a recording action.
    /// </summary>
    public bool IsActionTarget
    {
        get => isActionTarget;
        private set => SetProperty(ref isActionTarget, value);
    }

    /// <summary>
    /// Gets a value indicating whether this cell matches a recorded interest.
    /// </summary>
    public bool IsRecordedInterestMatch
    {
        get => isRecordedInterestMatch;
        private set => SetProperty(ref isRecordedInterestMatch, value);
    }

    /// <summary>
    /// Gets a value indicating whether this cell contains a manually marked value.
    /// </summary>
    public bool IsRecordedManualMatch
    {
        get => isRecordedManualMatch;
        private set => SetProperty(ref isRecordedManualMatch, value);
    }

    /// <summary>
    /// Gets a value indicating whether this cell was explicitly marked pertinent.
    /// </summary>
    public bool IsRecordedPertinent
    {
        get => isRecordedPertinent;
        private set => SetProperty(ref isRecordedPertinent, value);
    }

    /// <summary>
    /// Gets a value indicating whether this cell is the selected chain start.
    /// </summary>
    public bool IsChainStart
    {
        get => isChainStart;
        private set => SetProperty(ref isChainStart, value);
    }

    /// <summary>
    /// Gets a value indicating whether this cell is the selected chain end.
    /// </summary>
    public bool IsChainEnd
    {
        get => isChainEnd;
        private set => SetProperty(ref isChainEnd, value);
    }

    /// <summary>
    /// Gets a value indicating whether a recording annotation is visible.
    /// </summary>
    public bool HasRecordingAnnotation => IsRecordedInterestMatch
        || IsRecordedManualMatch
        || IsRecordedPertinent
        || IsChainStart
        || IsChainEnd;

    /// <summary>
    /// Gets accessible text describing recording annotations.
    /// </summary>
    public string RecordingAnnotationText
    {
        get
        {
            if (IsChainStart)
            {
                return "Chain start";
            }

            if (IsChainEnd)
            {
                return "Chain end";
            }

            if (IsRecordedPertinent)
            {
                return "Manually marked interesting";
            }

            if (IsRecordedManualMatch)
            {
                return "Contains a manually marked value";
            }

            return IsRecordedInterestMatch ? "Matches a recorded value of interest" : string.Empty;
        }
    }

    /// <summary>Gets the stable accent color for this cell's pertinent value.</summary>
    public string RecordingAccentHex
    {
        get => recordingAccentHex;
        private set => SetProperty(ref recordingAccentHex, value);
    }

    /// <summary>Gets the translucent pertinent-value highlight color.</summary>
    public string RecordingHighlightHex
    {
        get => recordingHighlightHex;
        private set => SetProperty(ref recordingHighlightHex, value);
    }

    /// <summary>
    /// Gets the conditional cell background color.
    /// </summary>
    public string BackgroundHex
    {
        get => backgroundHex;
        private set => SetProperty(ref backgroundHex, value);
    }

    /// <summary>
    /// Gets the bounded value text used inside larger automation descriptions.
    /// </summary>
    internal string AutomationValueText => TruncateForAutomation(Text);

    /// <summary>
    /// Replaces the conditional cell background color.
    /// </summary>
    /// <param name="colorHex">The ARGB or RGB color text.</param>
    internal void SetBackground(string colorHex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(colorHex);
        BackgroundHex = colorHex;
    }

    /// <summary>
    /// Replaces whether this cell is selected for a recording action.
    /// </summary>
    /// <param name="isSelected">Whether the cell is selected.</param>
    internal void SetActionTarget(bool isSelected)
    {
        IsActionTarget = isSelected;
    }

    /// <summary>
    /// Replaces non-color recording annotations for this cell.
    /// </summary>
    /// <param name="matchesInterest">Whether this value matches an active interest.</param>
    /// <param name="isPertinent">Whether the user explicitly marked this cell.</param>
    /// <param name="isStart">Whether this cell is the selected chain start.</param>
    /// <param name="isEnd">Whether this cell is the selected chain end.</param>
    /// <param name="matchesManualInterest">Whether this cell contains a manually marked value.</param>
    /// <param name="accentColorHex">The pertinent value accent color.</param>
    /// <param name="highlightColorHex">The translucent pertinent value highlight color.</param>
    internal void SetRecordingAnnotation(
        bool matchesInterest,
        bool isPertinent,
        bool isStart,
        bool isEnd,
        bool matchesManualInterest = false,
        string? accentColorHex = null,
        string? highlightColorHex = null)
    {
        IsRecordedInterestMatch = matchesInterest;
        IsRecordedManualMatch = matchesManualInterest;
        IsRecordedPertinent = isPertinent;
        IsChainStart = isStart;
        IsChainEnd = isEnd;
        bool hasAnnotation = matchesInterest || isPertinent || isStart || isEnd || matchesManualInterest;
        RecordingAccentHex = hasAnnotation ? accentColorHex ?? "#0078D4" : "#00000000";
        RecordingHighlightHex = hasAnnotation ? highlightColorHex ?? "#2E0078D4" : "#00000000";
        OnPropertyChanged(nameof(HasRecordingAnnotation));
        OnPropertyChanged(nameof(RecordingAnnotationText));
    }

    private static string TruncateForAutomation(string value)
    {
        const int MaximumLength = 160;
        return value.Length <= MaximumLength ? value : $"{value[..MaximumLength]}...";
    }
}
