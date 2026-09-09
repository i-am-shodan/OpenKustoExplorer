using OpenKustoExplorer.Graph;

namespace OpenKustoExplorer.Application.Assistance;

/// <summary>
/// Contains the active query-tab data shared with GitHub Copilot for one turn.
/// </summary>
public sealed class KustoCopilotContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoCopilotContext"/> class.
    /// </summary>
    /// <param name="documentId">The stable active-tab identifier.</param>
    /// <param name="documentTitle">The active tab title.</param>
    /// <param name="queryText">The complete active tab contents.</param>
    /// <param name="targetText">The concise cluster/database target.</param>
    /// <param name="schemaText">The active table and column schema summary.</param>
    /// <param name="sharedDataText">Optional bounded result data shared after explicit consent.</param>
    public KustoCopilotContext(
        Guid documentId,
        string documentTitle,
        string queryText,
        string targetText,
        string schemaText,
        string sharedDataText)
        : this(
            documentId,
            KustoCopilotScopeKind.Query,
            documentTitle,
            queryText,
            targetText,
            schemaText,
            sharedDataText,
            null,
            null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoCopilotContext"/> class for any workbench activity.
    /// </summary>
    /// <param name="documentId">The stable conversation scope identifier.</param>
    /// <param name="scopeKind">The workbench activity that owns the conversation.</param>
    /// <param name="documentTitle">The active scope title.</param>
    /// <param name="queryText">The complete KQL or openCypher contents.</param>
    /// <param name="targetText">The concise query target or graph summary.</param>
    /// <param name="schemaText">The bounded schema summary.</param>
    /// <param name="sharedDataText">Optional bounded data shared after explicit consent.</param>
    /// <param name="graphSnapshot">The graph snapshot for a Graph conversation.</param>
    /// <param name="recordedSessionScope">The pinned recorded-session scope, if any.</param>
    public KustoCopilotContext(
        Guid documentId,
        KustoCopilotScopeKind scopeKind,
        string documentTitle,
        string queryText,
        string targetText,
        string schemaText,
        string sharedDataText,
        GraphSnapshot? graphSnapshot,
        KustoCopilotRecordedSessionScope? recordedSessionScope = null)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(documentId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentTitle);
        ArgumentNullException.ThrowIfNull(queryText);
        ArgumentNullException.ThrowIfNull(targetText);
        ArgumentNullException.ThrowIfNull(schemaText);
        ArgumentNullException.ThrowIfNull(sharedDataText);

        DocumentId = documentId;
        ScopeKind = scopeKind;
        DocumentTitle = documentTitle;
        QueryText = queryText;
        TargetText = targetText;
        SchemaText = schemaText;
        SharedDataText = sharedDataText;
        GraphSnapshot = graphSnapshot;
        RecordedSessionScope = recordedSessionScope;
    }

    /// <summary>
    /// Gets the stable active-tab identifier.
    /// </summary>
    public Guid DocumentId { get; }

    /// <summary>
    /// Gets the workbench activity that owns this conversation.
    /// </summary>
    public KustoCopilotScopeKind ScopeKind { get; }

    /// <summary>
    /// Gets the active tab title.
    /// </summary>
    public string DocumentTitle { get; }

    /// <summary>
    /// Gets the complete active tab contents.
    /// </summary>
    public string QueryText { get; }

    /// <summary>
    /// Gets the concise cluster/database target.
    /// </summary>
    public string TargetText { get; }

    /// <summary>
    /// Gets the active table and column schema summary.
    /// </summary>
    public string SchemaText { get; }

    /// <summary>
    /// Gets optional bounded result data included only after explicit user consent.
    /// </summary>
    public string SharedDataText { get; }

    /// <summary>
    /// Gets the immutable graph snapshot for Graph scope, or <see langword="null"/> otherwise.
    /// </summary>
    public GraphSnapshot? GraphSnapshot { get; }

    /// <summary>
    /// Gets the pinned recorded-session scope, or <see langword="null"/> otherwise.
    /// </summary>
    public KustoCopilotRecordedSessionScope? RecordedSessionScope { get; }
}
