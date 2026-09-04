using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKustoExplorer.Application.Documents;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Presents one autosaved KQL document tab and its cluster/database target.
/// </summary>
public sealed class KustoDocumentViewModel : ObservableObject
{
    private static readonly string[] GroupColors =
    [
        "3675C8",
        "D64545",
        "27864A",
        "C56A16",
        "7656B5",
        "15858A",
        "B94D7D",
    ];

    private int caretPosition;
    private Uri? clusterUri;
    private string? databaseName;
    private string? groupName;
    private int formattingRevision;
    private KustoDocumentTabColor tabColor;
    private string text;
    private string title;
    private bool useAlternatingRows;

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoDocumentViewModel"/> class.
    /// </summary>
    /// <param name="document">The persisted immutable document.</param>
    /// <param name="closeAction">Closes this document tab.</param>
    /// <param name="renameAction">Opens the tab rename workflow.</param>
    /// <param name="groupAction">Opens the tab grouping workflow.</param>
    /// <param name="scheduleAction">Opens scheduling for the active query in this tab.</param>
    public KustoDocumentViewModel(
        KustoDocument document,
        Action<KustoDocumentViewModel> closeAction,
        Action<KustoDocumentViewModel> renameAction,
        Action<KustoDocumentViewModel> groupAction,
        Action<KustoDocumentViewModel> scheduleAction)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(closeAction);
        ArgumentNullException.ThrowIfNull(renameAction);
        ArgumentNullException.ThrowIfNull(groupAction);
        ArgumentNullException.ThrowIfNull(scheduleAction);

        Id = document.Id;
        title = document.Title;
        text = document.Text;
        caretPosition = document.CaretPosition;
        clusterUri = document.ClusterUri;
        databaseName = document.DatabaseName;
        tabColor = document.TabColor;
        groupName = document.GroupName;
        useAlternatingRows = document.UseAlternatingRows;
        ConditionalFormattingRules = new ObservableCollection<KustoConditionalFormatRuleViewModel>(
            document.ConditionalFormattingRules.Select(rule =>
                new KustoConditionalFormatRuleViewModel(rule, RemoveConditionalFormattingRule)));
        CloseCommand = new RelayCommand(() => closeAction(this));
        RenameCommand = new RelayCommand(() => renameAction(this));
        GroupCommand = new RelayCommand(() => groupAction(this));
        ScheduleCommand = new RelayCommand(() => scheduleAction(this));
        SetColorCommand = new RelayCommand<string>(SetTabColor);
    }

    /// <summary>
    /// Gets the stable document identifier.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Gets or sets the document tab title.
    /// </summary>
    public string Title
    {
        get => title;
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            if (SetProperty(ref title, value))
            {
                OnPropertyChanged(nameof(TabAutomationText));
            }
        }
    }

    /// <summary>
    /// Gets the optional user-defined group shown with the tab.
    /// </summary>
    public string? GroupName
    {
        get => groupName;
        private set
        {
            if (SetProperty(ref groupName, value))
            {
                OnPropertyChanged(nameof(HasGroup));
                OnPropertyChanged(nameof(TabAutomationText));
                OnPropertyChanged(nameof(GroupDisplayText));
                OnPropertyChanged(nameof(GroupBackgroundHex));
                OnPropertyChanged(nameof(GroupAccentHex));
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the tab belongs to a named group.
    /// </summary>
    public bool HasGroup => GroupName is not null;

    /// <summary>
    /// Gets the explicit group label shown below the tab title.
    /// </summary>
    public string GroupDisplayText => GroupName is null ? string.Empty : $"Group · {GroupName}";

    /// <summary>
    /// Gets a stable translucent group color shared by tabs with the same group name.
    /// </summary>
    public string GroupBackgroundHex
        => GroupName is null ? "#00000000" : $"#24{GetGroupColorHex(GroupName)}";

    /// <summary>
    /// Gets the opaque shared group accent used to connect adjacent grouped tabs.
    /// </summary>
    public string GroupAccentHex
        => GroupName is null ? "#00000000" : $"#{GetGroupColorHex(GroupName)}";

    /// <summary>
    /// Gets the selected tab accent color.
    /// </summary>
    public KustoDocumentTabColor TabColor
    {
        get => tabColor;
        private set
        {
            if (SetProperty(ref tabColor, value))
            {
                OnPropertyChanged(nameof(TabColorHex));
            }
        }
    }

    /// <summary>
    /// Gets the accessible color value used by the tab accent marker.
    /// </summary>
    public string TabColorHex => TabColor switch
    {
        KustoDocumentTabColor.Red => "#D64545",
        KustoDocumentTabColor.Orange => "#C56A16",
        KustoDocumentTabColor.Yellow => "#A47700",
        KustoDocumentTabColor.Green => "#27864A",
        KustoDocumentTabColor.Teal => "#15858A",
        KustoDocumentTabColor.Blue => "#3675C8",
        KustoDocumentTabColor.Purple => "#7656B5",
        KustoDocumentTabColor.Pink => "#B94D7D",
        _ => "#6B7280",
    };

    /// <summary>
    /// Gets the accessible tab description including its optional group.
    /// </summary>
    public string TabAutomationText => GroupName is null ? Title : $"{Title}, group {GroupName}";

    /// <summary>
    /// Gets or sets a value indicating whether alternating result rows are shaded.
    /// </summary>
    public bool UseAlternatingRows
    {
        get => useAlternatingRows;
        set
        {
            if (SetProperty(ref useAlternatingRows, value))
            {
                FormattingRevision++;
            }
        }
    }

    /// <summary>
    /// Gets tab-specific conditional-formatting rules.
    /// </summary>
    public ObservableCollection<KustoConditionalFormatRuleViewModel> ConditionalFormattingRules { get; }

    /// <summary>
    /// Gets the revision incremented whenever table formatting changes.
    /// </summary>
    public int FormattingRevision
    {
        get => formattingRevision;
        private set => SetProperty(ref formattingRevision, value);
    }

    /// <summary>
    /// Gets or sets the complete KQL document text.
    /// </summary>
    public string Text
    {
        get => text;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            bool caretChanged = caretPosition > value.Length;
            if (caretChanged)
            {
                caretPosition = value.Length;
            }

            if (SetProperty(ref text, value) && caretChanged)
            {
                OnPropertyChanged(nameof(CaretPosition));
            }
        }
    }

    /// <summary>
    /// Gets or sets the zero-based editor caret position.
    /// </summary>
    public int CaretPosition
    {
        get => caretPosition;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            int boundedPosition = Math.Min(value, Text.Length);
            SetProperty(ref caretPosition, boundedPosition);
        }
    }

    /// <summary>
    /// Gets the associated cluster URI, or <see langword="null"/>.
    /// </summary>
    public Uri? ClusterUri
    {
        get => clusterUri;
    }

    /// <summary>
    /// Gets the associated database name, or <see langword="null"/>.
    /// </summary>
    public string? DatabaseName
    {
        get => databaseName;
    }

    /// <summary>
    /// Gets the concise cluster/database target shown in the tab tooltip.
    /// </summary>
    public string TargetDisplayText => ClusterUri is null
        ? "No database selected"
        : $"{ClusterUri.Host} / {DatabaseName}";

    /// <summary>
    /// Gets the command that closes this document tab.
    /// </summary>
    public IRelayCommand CloseCommand { get; }

    /// <summary>
    /// Gets the command that opens the tab rename workflow.
    /// </summary>
    public IRelayCommand RenameCommand { get; }

    /// <summary>
    /// Gets the command that opens the tab grouping workflow.
    /// </summary>
    public IRelayCommand GroupCommand { get; }

    /// <summary>
    /// Gets the command that schedules the caret-selected query.
    /// </summary>
    public IRelayCommand ScheduleCommand { get; }

    /// <summary>
    /// Gets the command that changes the tab accent color by enum name.
    /// </summary>
    public IRelayCommand<string> SetColorCommand { get; }

    /// <summary>
    /// Clears the document execution target.
    /// </summary>
    internal void ClearTarget()
    {
        bool clusterChanged = clusterUri is not null;
        bool databaseChanged = databaseName is not null;
        clusterUri = null;
        databaseName = null;
        NotifyTargetChanged(clusterChanged, databaseChanged);
    }

    /// <summary>
    /// Creates an immutable persistence snapshot.
    /// </summary>
    /// <returns>The immutable document snapshot.</returns>
    internal KustoDocument CreateDocument()
    {
        int boundedCaretPosition = Math.Min(CaretPosition, Text.Length);
        return new KustoDocument(
            Id,
            Title,
            Text,
            boundedCaretPosition,
            ClusterUri,
            DatabaseName,
            TabColor,
            GroupName,
            UseAlternatingRows,
            ConditionalFormattingRules.Select(rule => rule.CreateRule()));
    }

    /// <summary>
    /// Associates the document with a cluster and database.
    /// </summary>
    /// <param name="targetClusterUri">The absolute cluster URI.</param>
    /// <param name="targetDatabaseName">The case-sensitive database name.</param>
    internal void SetTarget(Uri targetClusterUri, string targetDatabaseName)
    {
        ArgumentNullException.ThrowIfNull(targetClusterUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDatabaseName);

        bool clusterChanged = clusterUri != targetClusterUri;
        bool databaseChanged = !string.Equals(databaseName, targetDatabaseName, StringComparison.Ordinal);
        clusterUri = targetClusterUri;
        databaseName = targetDatabaseName;
        NotifyTargetChanged(clusterChanged, databaseChanged);
    }

    /// <summary>
    /// Assigns the document to a named group or removes it from its current group.
    /// </summary>
    /// <param name="newGroupName">The group name, or <see langword="null"/> for no group.</param>
    internal void SetGroup(string? newGroupName)
    {
        GroupName = string.IsNullOrWhiteSpace(newGroupName) ? null : newGroupName.Trim();
    }

    /// <summary>
    /// Adds a tab-specific conditional-formatting rule.
    /// </summary>
    /// <param name="rule">The validated immutable rule.</param>
    internal void AddConditionalFormattingRule(KustoConditionalFormatRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        ConditionalFormattingRules.Add(new KustoConditionalFormatRuleViewModel(
            rule,
            RemoveConditionalFormattingRule));
        FormattingRevision++;
    }

    private static string GetGroupColorHex(string name)
    {
        int hash = 17;
        foreach (char character in name.ToUpperInvariant())
        {
            hash = unchecked((hash * 31) + character);
        }

        return GroupColors[Math.Abs(hash % GroupColors.Length)];
    }

    private void RemoveConditionalFormattingRule(KustoConditionalFormatRuleViewModel rule)
    {
        if (ConditionalFormattingRules.Remove(rule))
        {
            FormattingRevision++;
        }
    }

    private void SetTabColor(string? colorName)
    {
        bool isKnownColor = Enum.TryParse(
            colorName,
            ignoreCase: true,
            out KustoDocumentTabColor selectedColor)
            && Enum.IsDefined(selectedColor);

        if (isKnownColor)
        {
            TabColor = selectedColor;
        }
    }

    private void NotifyTargetChanged(bool clusterChanged, bool databaseChanged)
    {
        if (clusterChanged)
        {
            OnPropertyChanged(nameof(ClusterUri));
        }

        if (databaseChanged)
        {
            OnPropertyChanged(nameof(DatabaseName));
        }

        if (clusterChanged || databaseChanged)
        {
            OnPropertyChanged(nameof(TargetDisplayText));
        }
    }
}
