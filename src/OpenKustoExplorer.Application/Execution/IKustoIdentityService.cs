namespace OpenKustoExplorer.Application.Execution;

/// <summary>
/// Exposes the process-lifetime accounts currently cached by Kusto authentication.
/// </summary>
public interface IKustoIdentityService
{
    /// <summary>
    /// Occurs after successful Kusto authentication changes the available account set.
    /// </summary>
    public event EventHandler? SignedInUsersChanged;

    /// <summary>
    /// Gets cached signed-in users and any profile photos available through silent Microsoft Graph access.
    /// </summary>
    /// <param name="cancellationToken">Cancels account and photo retrieval.</param>
    /// <returns>The distinct signed-in users.</returns>
    public Task<IReadOnlyList<KustoSignedInUser>> GetSignedInUsersAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes one account from every Kusto authentication cache used by this process.
    /// </summary>
    /// <param name="accountId">The stable home-account identifier to remove.</param>
    /// <param name="cancellationToken">Cancels account discovery.</param>
    /// <returns>A task that completes after the account is removed from every cache.</returns>
    public Task SignOutAsync(
        string accountId,
        CancellationToken cancellationToken = default);
}
