using OpenKustoExplorer.Application.Execution;

namespace OpenKustoExplorer.Application.Tests.Execution;

/// <summary>
/// Verifies signed-in account metadata invariants.
/// </summary>
public sealed class KustoSignedInUserTests
{
    /// <summary>
    /// Verifies profile bytes are copied and exposed read-only in process memory.
    /// </summary>
    [Fact]
    public void ConstructorCopiesProfilePhoto()
    {
        byte[] sourcePhoto = [1, 2, 3, 4];

        KustoSignedInUser user = new(
            "account-id",
            "Ada Lovelace",
            "ada@example.com",
            sourcePhoto);
        sourcePhoto[0] = 99;

        Assert.Equal([1, 2, 3, 4], user.ProfilePhoto.ToArray());
        Assert.True(user.HasProfilePhoto);
        Assert.Equal("Ada Lovelace", user.DisplayName);
    }
}
