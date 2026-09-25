namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Identifies how signed-in Kusto identities are owned by a host.
/// </summary>
public enum WorkbenchIdentityMode
{
    /// <summary>The workbench can interactively acquire and manage multiple accounts.</summary>
    InteractiveMultipleAccounts,

    /// <summary>The host supplies one authenticated account to the workbench.</summary>
    HostAuthenticatedAccount,
}
