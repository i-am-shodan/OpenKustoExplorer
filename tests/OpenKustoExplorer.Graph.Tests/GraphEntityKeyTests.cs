namespace OpenKustoExplorer.Graph.Tests;

/// <summary>
/// Verifies source-aware graph entity identity behavior.
/// </summary>
public sealed class GraphEntityKeyTests
{
    /// <summary>
    /// Verifies approved global identities compare by their normalized semantic fields.
    /// </summary>
    [Fact]
    public void GlobalKeysWithMatchingFieldsAreEqual()
    {
        GraphEntityKey first = new(GraphEntityKind.User, "User", "alice@example.com");
        GraphEntityKey second = new(GraphEntityKind.User, "User", "alice@example.com");

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.False(first.IsSourceScoped);
    }

    /// <summary>
    /// Verifies uncertain identities from different data sources do not merge accidentally.
    /// </summary>
    [Fact]
    public void SourceScopedKeysRemainDistinctAcrossSources()
    {
        GraphEntityKey first = new(GraphEntityKind.Unknown, "Asset", "server-1", "cluster-a/db");
        GraphEntityKey second = new(GraphEntityKind.Unknown, "Asset", "server-1", "cluster-b/db");

        Assert.NotEqual(first, second);
        Assert.True(first.IsSourceScoped);
        Assert.Contains("cluster-a/db", first.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies an entity key rejects missing identity material.
    /// </summary>
    [Fact]
    public void KeyRejectsMissingTypeOrIdentifier()
    {
        Assert.Throws<ArgumentException>(() => new GraphEntityKey(GraphEntityKind.Unknown, string.Empty, "id"));
        Assert.Throws<ArgumentException>(() => new GraphEntityKey(GraphEntityKind.Unknown, "Asset", string.Empty));
    }
}
