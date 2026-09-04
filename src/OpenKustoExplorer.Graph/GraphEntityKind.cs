namespace OpenKustoExplorer.Graph;

/// <summary>
/// Identifies the broad semantic category of a graph entity while preserving source-specific labels separately.
/// </summary>
public enum GraphEntityKind
{
    /// <summary>
    /// The entity category could not be inferred safely.
    /// </summary>
    Unknown,

    /// <summary>
    /// A generic identity or security principal.
    /// </summary>
    Identity,

    /// <summary>
    /// A person or user account.
    /// </summary>
    User,

    /// <summary>
    /// A group of identities or resources.
    /// </summary>
    Group,

    /// <summary>
    /// A role or permission set.
    /// </summary>
    Role,

    /// <summary>
    /// An application registration or software application.
    /// </summary>
    Application,

    /// <summary>
    /// A workload identity or service principal.
    /// </summary>
    ServicePrincipal,

    /// <summary>
    /// A credential, token, password, or secret.
    /// </summary>
    Credential,

    /// <summary>
    /// A public-key certificate.
    /// </summary>
    Certificate,

    /// <summary>
    /// A physical or logical host.
    /// </summary>
    Host,

    /// <summary>
    /// A managed endpoint or device.
    /// </summary>
    Device,

    /// <summary>
    /// A virtual machine or compute instance.
    /// </summary>
    VirtualMachine,

    /// <summary>
    /// An operating-system process.
    /// </summary>
    Process,

    /// <summary>
    /// A long-running service or daemon.
    /// </summary>
    Service,

    /// <summary>
    /// A container or workload unit.
    /// </summary>
    Container,

    /// <summary>
    /// An IP address.
    /// </summary>
    IpAddress,

    /// <summary>
    /// A DNS or directory domain.
    /// </summary>
    Domain,

    /// <summary>
    /// A uniform resource locator.
    /// </summary>
    Url,

    /// <summary>
    /// A network or subnet.
    /// </summary>
    Subnet,

    /// <summary>
    /// A network port or endpoint.
    /// </summary>
    Port,

    /// <summary>
    /// A cloud or identity tenant.
    /// </summary>
    CloudTenant,

    /// <summary>
    /// A cloud subscription or account boundary.
    /// </summary>
    Subscription,

    /// <summary>
    /// A cloud resource group or project.
    /// </summary>
    ResourceGroup,

    /// <summary>
    /// A generic cloud or infrastructure resource.
    /// </summary>
    CloudResource,

    /// <summary>
    /// An Azure Data Explorer cluster.
    /// </summary>
    Cluster,

    /// <summary>
    /// A database.
    /// </summary>
    Database,

    /// <summary>
    /// A table, materialized view, or external table.
    /// </summary>
    Table,

    /// <summary>
    /// A file or object.
    /// </summary>
    File,

    /// <summary>
    /// A cryptographic content hash.
    /// </summary>
    FileHash,

    /// <summary>
    /// An email message or address.
    /// </summary>
    Email,

    /// <summary>
    /// A security or operational alert.
    /// </summary>
    Alert,

    /// <summary>
    /// An incident or investigation case.
    /// </summary>
    Incident,

    /// <summary>
    /// A detection, finding, or anomaly.
    /// </summary>
    Detection,

    /// <summary>
    /// A vulnerability or weakness.
    /// </summary>
    Vulnerability,

    /// <summary>
    /// A threat actor or adversary.
    /// </summary>
    ThreatActor,

    /// <summary>
    /// Malware or another malicious artifact.
    /// </summary>
    Malware,

    /// <summary>
    /// A source-code repository.
    /// </summary>
    Repository,

    /// <summary>
    /// A source-control commit or change.
    /// </summary>
    Commit,

    /// <summary>
    /// A software build.
    /// </summary>
    Build,

    /// <summary>
    /// A deployment or release.
    /// </summary>
    Deployment,

    /// <summary>
    /// A distributed trace.
    /// </summary>
    Trace,

    /// <summary>
    /// A trace span or operation.
    /// </summary>
    Span,

    /// <summary>
    /// A raw or normalized log event.
    /// </summary>
    LogEvent,
}
