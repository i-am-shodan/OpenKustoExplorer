using OpenKustoExplorer.Application.Documents;

namespace OpenKustoExplorer.Application.Connections;

/// <summary>
/// Describes one open Microsoft Kusto Explorer query tab recovered from its local backup.
/// </summary>
public sealed class KustoExplorerImportedTab
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoExplorerImportedTab"/> class.
    /// </summary>
    /// <param name="id">The stable source tab identifier.</param>
    /// <param name="title">The source title or a generated fallback.</param>
    /// <param name="text">The exact query document contents.</param>
    /// <param name="caretPosition">The bounded source caret position.</param>
    /// <param name="order">The zero-based source tab order.</param>
    /// <param name="tabColor">The closest supported tab accent.</param>
    /// <param name="clusterUri">The optional validated cluster URI.</param>
    /// <param name="databaseName">The optional database name.</param>
    public KustoExplorerImportedTab(
        Guid id,
        string title,
        string text,
        int caretPosition,
        int order,
        KustoDocumentTabColor tabColor,
        Uri? clusterUri,
        string? databaseName)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(caretPosition);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(caretPosition, text.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(order);

        if (!Enum.IsDefined(tabColor))
        {
            throw new ArgumentOutOfRangeException(nameof(tabColor));
        }

        bool hasCluster = clusterUri is not null;
        bool hasDatabase = !string.IsNullOrWhiteSpace(databaseName);
        if (hasCluster != hasDatabase)
        {
            string parameterName = hasCluster ? nameof(databaseName) : nameof(clusterUri);
            throw new ArgumentException(
                "An imported tab target requires both cluster and database.",
                parameterName);
        }

        if (clusterUri is not null && (!clusterUri.IsAbsoluteUri || clusterUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("An imported tab cluster must be an absolute HTTPS URI.", nameof(clusterUri));
        }

        Id = id;
        Title = title.Trim();
        Text = text;
        CaretPosition = caretPosition;
        Order = order;
        TabColor = tabColor;
        ClusterUri = clusterUri;
        DatabaseName = string.IsNullOrWhiteSpace(databaseName) ? null : databaseName.Trim();
    }

    /// <summary>
    /// Gets the stable source tab identifier.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Gets the source title or generated fallback.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets the exact query document contents.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the bounded source caret position.
    /// </summary>
    public int CaretPosition { get; }

    /// <summary>
    /// Gets the zero-based source tab order.
    /// </summary>
    public int Order { get; }

    /// <summary>
    /// Gets the closest supported tab accent.
    /// </summary>
    public KustoDocumentTabColor TabColor { get; }

    /// <summary>
    /// Gets the optional validated cluster URI.
    /// </summary>
    public Uri? ClusterUri { get; }

    /// <summary>
    /// Gets the optional database name.
    /// </summary>
    public string? DatabaseName { get; }
}
