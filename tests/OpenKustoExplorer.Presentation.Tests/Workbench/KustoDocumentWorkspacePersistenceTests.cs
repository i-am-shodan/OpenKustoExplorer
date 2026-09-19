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
    /// <returns>A task that completes after the durable save fails.</returns>
    [Fact]
    public async Task FlushReportsUnexpectedStoreFailure()
    {
        using KustoDocumentWorkspacePersistence persistence = new(new ThrowingDocumentStore());
        string? error = null;
        persistence.SaveErrorChanged += value => error = value;

        await persistence.FlushAsync(new KustoDocumentWorkspace([], null));

        Assert.Contains("Queries are not saved", error, StringComparison.Ordinal);
        Assert.Contains("Unexpected store failure", error, StringComparison.Ordinal);
    }

    private sealed class ThrowingDocumentStore : IKustoDocumentStore
    {
        public KustoDocumentWorkspace Load() => new([], null);

        public Task SaveAsync(
            KustoDocumentWorkspace workspace,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException(new InvalidOperationException("Unexpected store failure"));
        }
    }
}
