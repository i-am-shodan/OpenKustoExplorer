namespace OpenKustoExplorer.Application.Execution;

/// <summary>
/// Describes how an analyst resolved a possible duplicate graph identity.
/// </summary>
public enum GraphIdentityResolutionDecision
{
    /// <summary>
    /// Merge the candidates into the suggested identity.
    /// </summary>
    Merge,

    /// <summary>
    /// Preserve every candidate as a separate graph node.
    /// </summary>
    KeepSeparate,

    /// <summary>
    /// Cancel the complete graph import without committing staged rows.
    /// </summary>
    Cancel,
}
