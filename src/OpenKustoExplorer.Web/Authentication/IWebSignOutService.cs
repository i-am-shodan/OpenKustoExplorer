namespace OpenKustoExplorer.Web.Authentication;

/// <summary>
/// Completes sign-out for the selected Web authentication mode.
/// </summary>
internal interface IWebSignOutService
{
    /// <summary>
    /// Creates the sign-out result after clearing any mode-specific token state.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels local cache cleanup.</param>
    /// <returns>The HTTP sign-out result.</returns>
    public Task<IResult> SignOutAsync(CancellationToken cancellationToken = default);
}
