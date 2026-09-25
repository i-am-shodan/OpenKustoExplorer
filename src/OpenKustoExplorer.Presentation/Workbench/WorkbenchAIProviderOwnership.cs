namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Identifies who selects and configures the workbench AI provider.
/// </summary>
public enum WorkbenchAIProviderOwnership
{
    /// <summary>The application user configures the provider.</summary>
    UserConfigured,

    /// <summary>The hosting service owns provider configuration and credentials.</summary>
    HostManaged,
}
