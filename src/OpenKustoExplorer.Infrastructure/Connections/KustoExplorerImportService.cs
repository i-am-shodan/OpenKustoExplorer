using System.Data.Common;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using OpenKustoExplorer.Application.Connections;
using OpenKustoExplorer.Application.Documents;

namespace OpenKustoExplorer.Infrastructure.Connections;

/// <summary>
/// Imports non-secret clusters and open tabs from Microsoft Kusto Explorer.
/// </summary>
public sealed class KustoExplorerImportService : IKustoExplorerImportService
{
    private const string ConnectionFileName = "UserConnections.xml";
    private const string GroupFileName = "UserConnectionGroups.xml";
    private const int MaximumQueryLength = 1024 * 1024;
    private const string RecoveryDirectoryName = "Recovery";
    private readonly string sourceDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoExplorerImportService"/> class for the current user.
    /// </summary>
    public KustoExplorerImportService()
        : this(GetDefaultSourceDirectory())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="KustoExplorerImportService"/> class.
    /// </summary>
    /// <param name="sourceDirectory">The legacy Kusto Explorer profile directory.</param>
    public KustoExplorerImportService(string sourceDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        this.sourceDirectory = Path.GetFullPath(sourceDirectory);
    }

    /// <inheritdoc />
    public async Task<KustoExplorerImportResult> ImportConnectionsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            return new KustoExplorerImportResult(false, [], 0);
        }

        IReadOnlyList<ConnectionSource> sources = await FindConnectionSourcesAsync(
            cancellationToken).ConfigureAwait(false);
        Dictionary<string, KustoClusterConnection> connections = new(StringComparer.OrdinalIgnoreCase);
        int skippedConnectionCount = 0;

        foreach (ConnectionSource source in sources)
        {
            XDocument? document = await LoadDocumentAsync(source.FilePath, cancellationToken)
                .ConfigureAwait(false);
            if (document is null)
            {
                skippedConnectionCount++;
            }
            else
            {
                skippedConnectionCount += AddConnections(document, source.FolderName, connections);
            }
        }

        TabImport tabImport = await ImportRecoveryTabsAsync(cancellationToken).ConfigureAwait(false);

        return new KustoExplorerImportResult(
            true,
            connections.Values,
            skippedConnectionCount,
            tabImport.Tabs,
            tabImport.SkippedTabCount);
    }

    private static int AddConnections(
        XDocument document,
        string? folderName,
        Dictionary<string, KustoClusterConnection> connections)
    {
        int skippedConnectionCount = 0;
        IEnumerable<XElement> entries = document.Root?.Elements()
            .Where(element => element.Name.LocalName == "ServerDescriptionBase") ?? [];

        foreach (XElement entry in entries)
        {
            if (TryCreateConnection(entry, folderName, out KustoClusterConnection? connection)
                && !connections.ContainsKey(GetAuthority(connection.ClusterUri)))
            {
                connections.Add(GetAuthority(connection.ClusterUri), connection);
            }
            else
            {
                skippedConnectionCount++;
            }
        }

        return skippedConnectionCount;
    }

    private static string GetAuthority(Uri clusterUri)
    {
        return clusterUri.GetLeftPart(UriPartial.Authority);
    }

    private static string GetDefaultSourceDirectory()
    {
        string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localApplicationData, "Kusto.Explorer");
    }

    private static XElement? GetElement(XElement parent, string localName)
    {
        return parent.Elements().FirstOrDefault(element => element.Name.LocalName == localName);
    }

    private static async Task<XDocument?> LoadDocumentAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        XDocument? document = null;

        try
        {
            await using FileStream stream = new(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            XmlReaderSettings settings = new()
            {
                Async = true,
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
            };
            using XmlReader reader = XmlReader.Create(stream, settings);
            document = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (IOException)
        {
            // An individual unreadable legacy file should not prevent importing other groups.
        }
        catch (UnauthorizedAccessException)
        {
            // An individual unreadable legacy file should not prevent importing other groups.
        }
        catch (XmlException)
        {
            // An individual malformed legacy file should not prevent importing other groups.
        }

        return document;
    }

    private static async Task<JsonDocument?> LoadJsonDocumentAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        JsonDocument? document = null;

        try
        {
            await using FileStream stream = new(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (IOException)
        {
            // One unreadable legacy JSON file does not prevent the remaining import.
        }
        catch (UnauthorizedAccessException)
        {
            // One unreadable legacy JSON file does not prevent the remaining import.
        }
        catch (JsonException)
        {
            // One malformed legacy JSON file does not prevent the remaining import.
        }

        return document;
    }

    private static bool TryCreateConnection(
        XElement entry,
        string? folderName,
        out KustoClusterConnection connection)
    {
        connection = null!;
        string displayName = GetElement(entry, "Name")?.Value.Trim() ?? string.Empty;
        string serializedConnection = GetElement(entry, "ConnectionString")?.Value.Trim() ?? string.Empty;
        bool valid = TryGetClusterUri(serializedConnection, out Uri? clusterUri);

        if (valid)
        {
            string effectiveDisplayName = string.IsNullOrWhiteSpace(displayName)
                ? clusterUri!.Host
                : displayName;
            connection = new KustoClusterConnection(clusterUri!, effectiveDisplayName, [], folderName);
        }

        return valid;
    }

    private static KustoDocumentTabColor ParseTabColor(string? colorTag)
    {
        return colorTag?.ToUpperInvariant() switch
        {
            "#FFFF0000" => KustoDocumentTabColor.Red,
            "#FF9BBB59" => KustoDocumentTabColor.Green,
            "#FF9ACCFF" => KustoDocumentTabColor.Blue,
            "#FF8064A2" or "#FFCBAFED" => KustoDocumentTabColor.Purple,
            _ => KustoDocumentTabColor.Default,
        };
    }

    private static bool TryGetClusterUri(string serializedConnection, out Uri? clusterUri)
    {
        clusterUri = null;
        string connectionString = serializedConnection.Trim();

        try
        {
            XDocument connectionDocument = XDocument.Parse(serializedConnection, LoadOptions.None);
            connectionString = connectionDocument.Root?.Value.Trim() ?? string.Empty;
        }
        catch (XmlException)
        {
            // Current Kusto Explorer versions store the connection string directly.
        }

        DbConnectionStringBuilder builder = new();
        bool parsed = true;

        try
        {
            builder.ConnectionString = connectionString;
        }
        catch (ArgumentException)
        {
            parsed = false;
        }

        object? sourceValue = null;
        Uri? parsedUri = null;
        bool hasSource = parsed && builder.TryGetValue("Data Source", out sourceValue);
        bool valid = hasSource
            && Uri.TryCreate(Convert.ToString(sourceValue, System.Globalization.CultureInfo.InvariantCulture), UriKind.Absolute, out parsedUri)
            && parsedUri.Scheme == Uri.UriSchemeHttps
            && !string.IsNullOrWhiteSpace(parsedUri.Host);

        if (valid)
        {
            clusterUri = new Uri(parsedUri!.GetLeftPart(UriPartial.Authority), UriKind.Absolute);
        }

        return valid;
    }

    private static void ResolveQueryTarget(
        string context,
        out Uri? clusterUri,
        out string? databaseName)
    {
        clusterUri = null;
        databaseName = null;
        DbConnectionStringBuilder builder = new();
        bool parsed = true;

        try
        {
            builder.ConnectionString = context;
        }
        catch (ArgumentException)
        {
            parsed = false;
        }

        object? sourceValue = null;
        object? databaseValue = null;
        Uri? parsedUri = null;
        bool valid = parsed
            && builder.TryGetValue("Data Source", out sourceValue)
            && builder.TryGetValue("Initial Catalog", out databaseValue)
            && Uri.TryCreate(Convert.ToString(sourceValue, System.Globalization.CultureInfo.InvariantCulture), UriKind.Absolute, out parsedUri)
            && parsedUri.Scheme == Uri.UriSchemeHttps
            && !string.IsNullOrWhiteSpace(parsedUri.Host)
            && !string.IsNullOrWhiteSpace(Convert.ToString(databaseValue, System.Globalization.CultureInfo.InvariantCulture));

        if (valid)
        {
            clusterUri = new Uri(parsedUri!.GetLeftPart(UriPartial.Authority), UriKind.Absolute);
            databaseName = Convert.ToString(databaseValue, System.Globalization.CultureInfo.InvariantCulture)!.Trim();
        }
    }

    private static bool TryCreateImportedTab(
        JsonElement root,
        out KustoExplorerImportedTab importedTab)
    {
        importedTab = null!;
        Guid id = Guid.Empty;
        int order = 0;
        bool hasText = TryGetJsonString(root, "QueryText", out string text);
        bool valid = root.ValueKind == JsonValueKind.Object
            && GetJsonBoolean(root, "IsOpen")
            && TryGetJsonGuid(root, "Id", out id)
            && hasText
            && text.Length <= MaximumQueryLength
            && TryGetNonnegativeJsonInt32(root, "TabOrdinal", out order);

        if (valid)
        {
            _ = TryGetJsonString(root, "CustomTitle", out string customTitle);
            _ = TryGetJsonString(root, "FullPath", out string fullPath);
            string title = GetImportedTabTitle(customTitle, fullPath, order);
            int position = GetJsonInt32(root, "Position");
            int caretPosition = Math.Clamp(position, 0, text.Length);
            _ = TryGetJsonString(root, "ConnectionString", out string context);
            _ = TryGetJsonString(root, "ColorTag", out string colorTag);
            ResolveQueryTarget(context, out Uri? clusterUri, out string? databaseName);
            importedTab = new KustoExplorerImportedTab(
                id,
                title,
                text,
                caretPosition,
                order,
                ParseTabColor(colorTag),
                clusterUri,
                databaseName);
        }

        return valid;
    }

    private static bool GetJsonBoolean(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out JsonElement element)
            && element.ValueKind == JsonValueKind.True;
    }

    private static int GetJsonInt32(JsonElement root, string propertyName)
    {
        int value = 0;
        _ = root.TryGetProperty(propertyName, out JsonElement element)
            && element.TryGetInt32(out value);
        return value;
    }

    private static bool TryGetJsonGuid(
        JsonElement root,
        string propertyName,
        out Guid value)
    {
        value = Guid.Empty;
        return TryGetJsonString(root, propertyName, out string text)
            && Guid.TryParse(text, out value)
            && value != Guid.Empty;
    }

    private static bool TryGetJsonString(
        JsonElement root,
        string propertyName,
        out string value)
    {
        value = string.Empty;
        JsonElement element = default;
        bool hasValue = root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(propertyName, out element)
            && element.ValueKind == JsonValueKind.String;

        if (hasValue)
        {
            value = element.GetString() ?? string.Empty;
        }

        return hasValue;
    }

    private static bool TryGetNonnegativeJsonInt32(
        JsonElement root,
        string propertyName,
        out int value)
    {
        value = 0;
        return root.TryGetProperty(propertyName, out JsonElement element)
            && element.TryGetInt32(out value)
            && value >= 0;
    }

    private static string GetImportedTabTitle(string customTitle, string fullPath, int order)
    {
        string title = customTitle.Trim();

        if (title.Length == 0 && fullPath.Length > 0)
        {
            title = Path.GetFileNameWithoutExtension(fullPath.Trim());
        }

        if (title.Length == 0)
        {
            title = $"Imported tab {order + 1:N0}";
        }

        return title;
    }

    private static async Task<KustoExplorerImportedTab?> LoadImportedTabAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        JsonDocument? document = await LoadJsonDocumentAsync(filePath, cancellationToken)
            .ConfigureAwait(false);
        KustoExplorerImportedTab? tab = null;

        if (document is not null)
        {
            using (document)
            {
                _ = TryCreateImportedTab(document.RootElement, out tab!);
            }
        }

        return tab;
    }

    private async Task<TabImport> ImportRecoveryTabsAsync(CancellationToken cancellationToken)
    {
        string recoveryDirectoryPath = Path.Combine(sourceDirectory, RecoveryDirectoryName);
        List<KustoExplorerImportedTab> tabs = [];
        HashSet<Guid> tabIds = [];
        int skippedTabCount = 0;

        if (Directory.Exists(recoveryDirectoryPath))
        {
            IEnumerable<string> filePaths = Directory.EnumerateFiles(
                recoveryDirectoryPath,
                "*.kebak",
                SearchOption.TopDirectoryOnly);

            foreach (string filePath in filePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                KustoExplorerImportedTab? tab = await LoadImportedTabAsync(filePath, cancellationToken)
                    .ConfigureAwait(false);

                if (tab is not null && tabIds.Add(tab.Id))
                {
                    tabs.Add(tab);
                }
                else
                {
                    skippedTabCount++;
                }
            }
        }

        KustoExplorerImportedTab[] orderedTabs = tabs
            .OrderBy(tab => tab.Order)
            .ToArray();
        return new TabImport(Array.AsReadOnly(orderedTabs), skippedTabCount);
    }

    private async Task<IReadOnlyList<ConnectionSource>> FindConnectionSourcesAsync(
        CancellationToken cancellationToken)
    {
        List<ConnectionSource> sources = [];
        string groupFilePath = Path.Combine(sourceDirectory, GroupFileName);
        XDocument? groupDocument = File.Exists(groupFilePath)
            ? await LoadDocumentAsync(groupFilePath, cancellationToken).ConfigureAwait(false)
            : null;

        if (groupDocument?.Root is not null)
        {
            foreach (XElement group in groupDocument.Root.Elements())
            {
                string? folderName = GetElement(group, "Name")?.Value.Trim();
                string? filePath = GetElement(group, "Details")?.Value.Trim();
                AddSource(sources, filePath, folderName);
            }
        }

        EnumerationOptions options = new()
        {
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
        };

        foreach (string filePath in Directory.EnumerateFiles(sourceDirectory, ConnectionFileName, options))
        {
            AddSource(sources, filePath, null);
        }

        return sources.AsReadOnly();
    }

    private void AddSource(
        ICollection<ConnectionSource> sources,
        string? filePath,
        string? folderName)
    {
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            string fullPath = Path.GetFullPath(filePath);
            string relativePath = Path.GetRelativePath(sourceDirectory, fullPath);
            bool isInsideSource = relativePath != ".."
                && !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
            bool alreadyAdded = sources.Any(source => string.Equals(
                source.FilePath,
                fullPath,
                StringComparison.OrdinalIgnoreCase));

            if (isInsideSource && File.Exists(fullPath) && !alreadyAdded)
            {
                sources.Add(new ConnectionSource(fullPath, folderName));
            }
        }
    }

    private sealed class ConnectionSource
    {
        internal ConnectionSource(string filePath, string? folderName)
        {
            FilePath = filePath;
            FolderName = string.IsNullOrWhiteSpace(folderName) ? null : folderName.Trim();
        }

        internal string FilePath { get; }

        internal string? FolderName { get; }
    }

    private sealed class TabImport
    {
        internal TabImport(
            IReadOnlyList<KustoExplorerImportedTab> tabs,
            int skippedTabCount)
        {
            Tabs = tabs;
            SkippedTabCount = skippedTabCount;
        }

        internal IReadOnlyList<KustoExplorerImportedTab> Tabs { get; }

        internal int SkippedTabCount { get; }
    }
}
