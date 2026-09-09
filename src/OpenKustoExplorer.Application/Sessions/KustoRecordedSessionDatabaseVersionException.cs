namespace OpenKustoExplorer.Application.Sessions;

/// <summary>
/// Indicates that recorded-session storage uses an unsupported schema version.
/// </summary>
public sealed class KustoRecordedSessionDatabaseVersionException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KustoRecordedSessionDatabaseVersionException"/> class.
    /// </summary>
    /// <param name="databaseVersion">The version stored in the database.</param>
    /// <param name="supportedVersion">The version supported by the application.</param>
    public KustoRecordedSessionDatabaseVersionException(int databaseVersion, int supportedVersion)
        : base(
            $"Recorded-session database schema version {databaseVersion} is newer than supported version {supportedVersion}.")
    {
        DatabaseVersion = databaseVersion;
        SupportedVersion = supportedVersion;
    }

    /// <summary>Gets the version stored in the database.</summary>
    public int DatabaseVersion { get; }

    /// <summary>Gets the version supported by the application.</summary>
    public int SupportedVersion { get; }
}
