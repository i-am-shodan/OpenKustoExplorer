using OpenKustoExplorer.Application.Documents;
using OpenKustoExplorer.Presentation.Workbench;

namespace OpenKustoExplorer.Presentation.Tests.Workbench;

/// <summary>
/// Verifies document persistence failure containment.
/// </summary>
public sealed class KustoDocumentWorkspacePersistenceTests
{
    /// <summary>
    /// Verifies an unexpected store failure is exposed without escaping the persistence boundary.
    /// </summary>
    [Fact]
    public void FlushReportsUnexpectedStoreFailure()
    {
        using KustoDocumentWorkspacePersistence persistence = new(new ThrowingDocumentStore());
        string? error = null;
        persistence.SaveErrorChanged += value => error = value;

        persistence.Flush(new KustoDocumentWorkspace([], null));

        Assert.Contains("Queries are not saved", error, StringComparison.Ordinal);
        Assert.Contains("Unexpected store failure", error, StringComparison.Ordinal);
    }

    private sealed class ThrowingDocumentStore : IKustoDocumentStore
    {
        public KustoDocumentWorkspace Load() => new([], null);

        public void Save(KustoDocumentWorkspace workspace)
        {
            throw new InvalidOperationException("Unexpected store failure");
        }
    }
}
