using Avalonia.Media.Imaging;
using OpenKustoExplorer.Application.Execution;

namespace OpenKustoExplorer.Desktop;

/// <summary>
/// Presents one signed-in Kusto account as a disposable desktop avatar.
/// </summary>
public sealed class SignedInUserAvatarViewModel : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SignedInUserAvatarViewModel"/> class.
    /// </summary>
    /// <param name="user">The application-owned signed-in user.</param>
    internal SignedInUserAvatarViewModel(KustoSignedInUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        AccountId = user.AccountId;
        DisplayName = user.DisplayName;
        AccountName = user.AccountName;
        ToolTipText = string.Equals(user.DisplayName, user.AccountName, StringComparison.OrdinalIgnoreCase)
            ? user.AccountName
            : $"{user.DisplayName}\n{user.AccountName}";

        if (user.HasProfilePhoto)
        {
            try
            {
                using MemoryStream stream = new(user.ProfilePhoto.ToArray(), writable: false);
                ProfilePhoto = new Bitmap(stream);
            }
            catch (ArgumentException)
            {
                // Invalid optional image data falls back to the account icon.
            }
        }
    }

    /// <summary>
    /// Gets the stable account identifier used for sign-out.
    /// </summary>
    public string AccountId { get; }

    /// <summary>
    /// Gets the user's best available display name.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets the account sign-in name.
    /// </summary>
    public string AccountName { get; }

    /// <summary>
    /// Gets the accessible account description.
    /// </summary>
    public string AutomationName => $"Signed in as {DisplayName}, {AccountName}";

    /// <summary>
    /// Gets the optional decoded profile photo.
    /// </summary>
    public Bitmap? ProfilePhoto { get; }

    /// <summary>
    /// Gets a value indicating whether a decoded profile photo is available.
    /// </summary>
    public bool HasProfilePhoto => ProfilePhoto is not null;

    /// <summary>
    /// Gets the account tooltip.
    /// </summary>
    public string ToolTipText { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        ProfilePhoto?.Dispose();
    }
}
