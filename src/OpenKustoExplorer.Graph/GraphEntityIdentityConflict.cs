using System.Collections.ObjectModel;

namespace OpenKustoExplorer.Graph;

/// <summary>
/// Presents graph identities that may describe the same logical node.
/// </summary>
public sealed class GraphEntityIdentityConflict
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GraphEntityIdentityConflict"/> class.
    /// </summary>
    /// <param name="matchKind">The value category that matched.</param>
    /// <param name="matchValue">The analyst-facing matched value.</param>
    /// <param name="candidates">The distinct candidate identities.</param>
    /// <param name="suggestedEntity">The identity used if the candidates are merged.</param>
    public GraphEntityIdentityConflict(
        GraphEntityIdentityMatchKind matchKind,
        string matchValue,
        IEnumerable<GraphEntityIdentityCandidate> candidates,
        GraphEntityKey suggestedEntity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(matchValue);
        ArgumentNullException.ThrowIfNull(candidates);
        GraphEntityIdentityCandidate[] candidateSnapshot = candidates.ToArray();

        if (candidateSnapshot.Select(candidate => candidate.Entity).Distinct().Count() < 2)
        {
            throw new ArgumentException("An identity conflict requires at least two distinct entities.", nameof(candidates));
        }

        if (!candidateSnapshot.Any(candidate => candidate.Entity == suggestedEntity))
        {
            throw new ArgumentException("The suggested identity must be one of the candidates.", nameof(suggestedEntity));
        }

        MatchKind = matchKind;
        MatchValue = matchValue.Trim();
        Candidates = Array.AsReadOnly(candidateSnapshot);
        SuggestedEntity = suggestedEntity;
    }

    /// <summary>
    /// Gets the distinct candidate identities.
    /// </summary>
    public ReadOnlyCollection<GraphEntityIdentityCandidate> Candidates { get; }

    /// <summary>
    /// Gets the value category that matched.
    /// </summary>
    public GraphEntityIdentityMatchKind MatchKind { get; }

    /// <summary>
    /// Gets the analyst-facing matched value.
    /// </summary>
    public string MatchValue { get; }

    /// <summary>
    /// Gets the identity used if the candidates are merged.
    /// </summary>
    public GraphEntityKey SuggestedEntity { get; }
}
