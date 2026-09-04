namespace OpenKustoExplorer.Application.Documents;

/// <summary>
/// Contains all persisted document tabs and the selected document identifier.
/// </summary>
public sealed class KustoDocumentWorkspace
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoDocumentWorkspace"/> class.
    /// </summary>
    /// <param name="documents">The open documents in tab order.</param>
    /// <param name="selectedDocumentId">The selected document identifier, or <see langword="null"/>.</param>
    public KustoDocumentWorkspace(IEnumerable<KustoDocument> documents, Guid? selectedDocumentId)
    {
        ArgumentNullException.ThrowIfNull(documents);

        KustoDocument[] documentArray = documents.ToArray();
        bool hasDuplicateIds = documentArray
            .GroupBy(document => document.Id)
            .Any(group => group.Count() > 1);
        if (hasDuplicateIds)
        {
            throw new ArgumentException("Document identifiers must be unique.", nameof(documents));
        }

        bool selectedDocumentExists = selectedDocumentId is null
            || documentArray.Any(document => document.Id == selectedDocumentId);
        if (!selectedDocumentExists)
        {
            throw new ArgumentException("The selected document must exist in the workspace.", nameof(selectedDocumentId));
        }

        Documents = Array.AsReadOnly(documentArray);
        SelectedDocumentId = selectedDocumentId;
    }

    /// <summary>
    /// Gets the open documents in tab order.
    /// </summary>
    public IReadOnlyList<KustoDocument> Documents { get; }

    /// <summary>
    /// Gets the selected document identifier, or <see langword="null"/>.
    /// </summary>
    public Guid? SelectedDocumentId { get; }
}
