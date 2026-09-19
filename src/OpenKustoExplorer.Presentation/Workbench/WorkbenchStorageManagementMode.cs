namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Identifies the storage-management surface supplied by a host.
/// </summary>
public enum WorkbenchStorageManagementMode
{
    /// <summary>The host exposes its local application-data folder.</summary>
    LocalDataFolder,

    /// <summary>The host exposes browser storage status, backup, restore, and reset.</summary>
    BrowserDialog,
}
