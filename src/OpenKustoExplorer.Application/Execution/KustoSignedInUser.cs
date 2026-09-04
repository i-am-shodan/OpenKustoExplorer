namespace OpenKustoExplorer.Application.Execution;

/// <summary>
/// Describes one signed-in account and its optional in-memory profile photo.
/// </summary>
public sealed class KustoSignedInUser
{
    private readonly byte[] profilePhoto;

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoSignedInUser"/> class.
    /// </summary>
    /// <param name="accountId">The stable home-account identifier.</param>
    /// <param name="displayName">The best available display name.</param>
    /// <param name="accountName">The account's sign-in name.</param>
    /// <param name="profilePhoto">Optional encoded image bytes held only in memory.</param>
    public KustoSignedInUser(
        string accountId,
        string displayName,
        string accountName,
        ReadOnlySpan<byte> profilePhoto)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);

        AccountId = accountId;
        DisplayName = displayName.Trim();
        AccountName = accountName.Trim();
        this.profilePhoto = profilePhoto.ToArray();
    }

    /// <summary>
    /// Gets the stable home-account identifier.
    /// </summary>
    public string AccountId { get; }

    /// <summary>
    /// Gets the best available display name.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets the account's sign-in name.
    /// </summary>
    public string AccountName { get; }

    /// <summary>
    /// Gets optional encoded profile image bytes held only in process memory.
    /// </summary>
    public ReadOnlyMemory<byte> ProfilePhoto => profilePhoto;

    /// <summary>
    /// Gets a value indicating whether a profile photo is available.
    /// </summary>
    public bool HasProfilePhoto => profilePhoto.Length > 0;
}
