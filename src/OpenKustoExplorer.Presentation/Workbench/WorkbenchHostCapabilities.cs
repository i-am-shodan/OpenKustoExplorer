using OpenKustoExplorer.Application.Assistance;

namespace OpenKustoExplorer.Presentation.Workbench;

/// <summary>
/// Describes the explicit platform capabilities supplied to one shared workbench.
/// </summary>
public sealed class WorkbenchHostCapabilities
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WorkbenchHostCapabilities"/> class.
    /// </summary>
    /// <param name="importMode">How the host obtains Kusto Explorer profile data.</param>
    /// <param name="supportsToastNotifications">Whether in-app notifications are available.</param>
    /// <param name="supportsEmailNotifications">Whether SMTP delivery is available.</param>
    /// <param name="supportsApplicationLaunchNotifications">Whether local application actions are available.</param>
    /// <param name="toastNotificationName">The host-specific in-app notification label.</param>
    /// <param name="aiProviderOwnership">Who configures the AI provider.</param>
    /// <param name="managedAIProviderKind">The provider selected by the host, when applicable.</param>
    /// <param name="managedAIProviderDisplayName">The host-managed provider display name.</param>
    /// <param name="supportsMcp">Whether configured MCP servers can be enabled.</param>
    /// <param name="identityMode">How authenticated identities are owned.</param>
    /// <param name="storageManagementMode">How users manage durable application state.</param>
    /// <param name="maximumGraphEntityCount">The maximum entities in one generation.</param>
    /// <param name="maximumGraphRelationshipCount">The maximum relationships in one generation.</param>
    /// <param name="supportsWebhookNotifications">Whether HTTPS webhook delivery is available.</param>
    public WorkbenchHostCapabilities(
        KustoExplorerImportMode importMode,
        bool supportsToastNotifications,
        bool supportsEmailNotifications,
        bool supportsApplicationLaunchNotifications,
        string toastNotificationName,
        WorkbenchAIProviderOwnership aiProviderOwnership,
        KustoAIProviderKind? managedAIProviderKind,
        string? managedAIProviderDisplayName,
        bool supportsMcp,
        WorkbenchIdentityMode identityMode,
        WorkbenchStorageManagementMode storageManagementMode,
        int maximumGraphEntityCount,
        int maximumGraphRelationshipCount,
        bool supportsWebhookNotifications = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumGraphEntityCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumGraphRelationshipCount);
        if (!Enum.IsDefined(importMode))
        {
            throw new ArgumentOutOfRangeException(nameof(importMode));
        }

        if (!Enum.IsDefined(aiProviderOwnership))
        {
            throw new ArgumentOutOfRangeException(nameof(aiProviderOwnership));
        }

        if (!Enum.IsDefined(identityMode))
        {
            throw new ArgumentOutOfRangeException(nameof(identityMode));
        }

        if (!Enum.IsDefined(storageManagementMode))
        {
            throw new ArgumentOutOfRangeException(nameof(storageManagementMode));
        }

        if (supportsToastNotifications && string.IsNullOrWhiteSpace(toastNotificationName))
        {
            throw new ArgumentException(
                "A host with toast notifications must provide a channel name.",
                nameof(toastNotificationName));
        }

        bool hasManagedProvider = managedAIProviderKind is not null
            && Enum.IsDefined(managedAIProviderKind.Value)
            && !string.IsNullOrWhiteSpace(managedAIProviderDisplayName);
        if ((aiProviderOwnership == WorkbenchAIProviderOwnership.HostManaged) != hasManagedProvider)
        {
            throw new ArgumentException(
                "Host-managed AI requires one valid provider kind and display name.",
                nameof(managedAIProviderKind));
        }

        ImportMode = importMode;
        SupportsToastNotifications = supportsToastNotifications;
        SupportsEmailNotifications = supportsEmailNotifications;
        SupportsApplicationLaunchNotifications = supportsApplicationLaunchNotifications;
        SupportsWebhookNotifications = supportsWebhookNotifications;
        ToastNotificationName = toastNotificationName?.Trim() ?? string.Empty;
        AIProviderOwnership = aiProviderOwnership;
        ManagedAIProviderKind = managedAIProviderKind;
        ManagedAIProviderDisplayName = managedAIProviderDisplayName?.Trim() ?? string.Empty;
        SupportsMcp = supportsMcp;
        IdentityMode = identityMode;
        StorageManagementMode = storageManagementMode;
        MaximumGraphEntityCount = maximumGraphEntityCount;
        MaximumGraphRelationshipCount = maximumGraphRelationshipCount;
    }

    /// <summary>Gets the host's AI provider-configuration ownership.</summary>
    public WorkbenchAIProviderOwnership AIProviderOwnership { get; }

    /// <summary>Gets the host's Kusto Explorer import mode.</summary>
    public KustoExplorerImportMode ImportMode { get; }

    /// <summary>Gets the host's identity ownership mode.</summary>
    public WorkbenchIdentityMode IdentityMode { get; }

    /// <summary>Gets a value indicating whether AI provider settings are user-configurable.</summary>
    public bool IsAIProviderUserConfigured => AIProviderOwnership == WorkbenchAIProviderOwnership.UserConfigured;

    /// <summary>Gets the host-managed provider display name, or an empty string.</summary>
    public string ManagedAIProviderDisplayName { get; }

    /// <summary>Gets the host-managed provider kind, when applicable.</summary>
    public KustoAIProviderKind? ManagedAIProviderKind { get; }

    /// <summary>Gets the maximum entities retained in one graph generation.</summary>
    public int MaximumGraphEntityCount { get; }

    /// <summary>Gets the maximum relationships retained in one graph generation.</summary>
    public int MaximumGraphRelationshipCount { get; }

    /// <summary>Gets the host storage-management mode.</summary>
    public WorkbenchStorageManagementMode StorageManagementMode { get; }

    /// <summary>Gets a value indicating whether local application notification launch is supported.</summary>
    public bool SupportsApplicationLaunchNotifications { get; }

    /// <summary>Gets a value indicating whether SMTP notification delivery is supported.</summary>
    public bool SupportsEmailNotifications { get; }

    /// <summary>Gets a value indicating whether Kusto Explorer import is available.</summary>
    public bool SupportsKustoExplorerImport => ImportMode != KustoExplorerImportMode.Unavailable;

    /// <summary>Gets a value indicating whether configured MCP servers are supported.</summary>
    public bool SupportsMcp { get; }

    /// <summary>Gets a value indicating whether an in-app toast channel is supported.</summary>
    public bool SupportsToastNotifications { get; }

    /// <summary>Gets a value indicating whether HTTPS webhook delivery is supported.</summary>
    public bool SupportsWebhookNotifications { get; }

    /// <summary>Gets the host-specific in-app toast channel name.</summary>
    public string ToastNotificationName { get; }
}
