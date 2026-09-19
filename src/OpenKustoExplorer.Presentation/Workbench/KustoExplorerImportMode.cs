namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Identifies how a host can obtain Microsoft Kusto Explorer profile data.
/// </summary>
public enum KustoExplorerImportMode
{
    /// <summary>The host cannot import a profile.</summary>
    Unavailable,

    /// <summary>The host discovers the current user's local profile.</summary>
    LocalProfileDiscovery,

    /// <summary>The user explicitly selects a profile folder.</summary>
    UserSelectedProfileFolder,
}
