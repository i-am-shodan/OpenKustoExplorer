namespace OpenKustoExplorer.Application.Sessions;

/// <summary>
/// Provides explicit maintenance operations for local recorded-session storage.
/// </summary>
public interface IKustoRecordedSessionStoreMaintenance
{
    /// <summary>Gets the directory containing recorded-session storage.</summary>
    public string DatabaseDirectoryPath { get; }

    /// <summary>Deletes and recreates recorded-session storage.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes after empty storage is ready.</returns>
    public Task ResetDatabaseAsync(CancellationToken cancellationToken = default);
}
