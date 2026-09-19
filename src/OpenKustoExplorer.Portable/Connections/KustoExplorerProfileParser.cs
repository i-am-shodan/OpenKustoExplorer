using System.Collections.ObjectModel;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using OpenKustoExplorer.Application.Connections;
using OpenKustoExplorer.Application.Documents;

namespace OpenKustoExplorer.Portable.Connections;

/// <summary>
/// Parses non-secret connection metadata and open tabs from selected Kusto Explorer profile files.
/// </summary>
public static class KustoExplorerProfileParser
{
    private const string ConnectionFileName = "UserConnections.xml";
    private const string GroupFileName = "UserConnectionGroups.xml";
    private const int MaximumQueryLength = 1024 * 1024;
    private const long MaximumXmlCharacterCount = 16L * 1024 * 1024;
    private const string RecoveryDirectoryName = "Recovery";

    /// <summary>
    /// Parses one explicitly selected profile without reading credentials, history, cached results, or settings.
    /// </summary>
    /// <param name="files">The selected profile files.</param>
    /// <returns>The validated import result.</returns>
    public static KustoExplorerImportResult Parse(IEnumerable<KustoExplorerProfileFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        KustoExplorerProfileFile[] selectedFiles = files.ToArray();
        if (selectedFiles.Any(file => file is null))
        {
            throw new ArgumentException("The selected profile contains a null file.", nameof(files));
        }

        KustoExplorerProfileFile[] connectionFiles = selectedFiles
            .Where(file => string.Equals(GetFileName(file.RelativePath), ConnectionFileName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        IReadOnlyList<ConnectionSource> sources = CreateConnectionSources(selectedFiles, connectionFiles);
        Dictionary<string, KustoClusterConnection> connections = new(StringComparer.OrdinalIgnoreCase);
        int skippedConnectionCount = 0;

        foreach (ConnectionSource source in sources)
        {
            XDocument? document = TryLoadXml(source.File.Content);
            skippedConnectionCount += document is null
                ? 1
                : AddConnections(document, source.FolderName, connections);
        }

        List<KustoExplorerImportedTab> tabs = [];
        HashSet<Guid> tabIds = [];
        int skippedTabCount = 0;
        foreach (KustoExplorerProfileFile file in selectedFiles
            .Where(IsRecoveryTab)
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            if (TryCreateImportedTab(file.Content, out KustoExplorerImportedTab? tab)
                && tabIds.Add(tab.Id))
            {
                tabs.Add(tab);
            }
            else
            {
                skippedTabCount++;
            }
        }

        return new KustoExplorerImportResult(
            selectedFiles.Length > 0,
            connections.Values,
            skippedConnectionCount,
            tabs,
            skippedTabCount);
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

    private static ReadOnlyCollection<ConnectionSource> CreateConnectionSources(
        IReadOnlyList<KustoExplorerProfileFile> selectedFiles,
        IReadOnlyList<KustoExplorerProfileFile> connectionFiles)
    {
        List<ConnectionSource> sources = [];
        HashSet<string> addedPaths = new(StringComparer.OrdinalIgnoreCase);
        KustoExplorerProfileFile? groupFile = selectedFiles
            .Where(file => string.Equals(GetFileName(file.RelativePath), GroupFileName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => GetPathSegments(file.RelativePath).Length)
            .ThenBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        XDocument? groupDocument = groupFile is null ? null : TryLoadXml(groupFile.Content);

        if (groupDocument?.Root is not null)
        {
            foreach (XElement group in groupDocument.Root.Elements())
            {
                string? folderName = GetElement(group, "Name")?.Value.Trim();
                string? referencedPath = GetElement(group, "Details")?.Value.Trim();
                KustoExplorerProfileFile? source = FindReferencedFile(referencedPath, connectionFiles);
                if (source is not null && addedPaths.Add(source.RelativePath))
                {
                    sources.Add(new ConnectionSource(source, folderName));
                }
            }
        }

        sources.AddRange(connectionFiles
            .Where(file => addedPaths.Add(file.RelativePath))
            .Select(file => new ConnectionSource(file, null)));

        return sources.AsReadOnly();
    }

    private static KustoExplorerProfileFile? FindReferencedFile(
        string? referencedPath,
        IReadOnlyList<KustoExplorerProfileFile> candidates)
    {
        if (string.IsNullOrWhiteSpace(referencedPath))
        {
            return null;
        }

        string[] referenceSegments = GetPathSegments(referencedPath);
        KustoExplorerProfileFile? bestMatch = null;
        int bestScore = 0;
        bool ambiguous = false;
        foreach (KustoExplorerProfileFile candidate in candidates)
        {
            int score = CountMatchingTrailingSegments(
                referenceSegments,
                GetPathSegments(candidate.RelativePath));
            if (score > bestScore)
            {
                bestMatch = candidate;
                bestScore = score;
                ambiguous = false;
            }
            else if (score > 0 && score == bestScore)
            {
                ambiguous = true;
            }
        }

        return bestScore > 0 && !ambiguous ? bestMatch : null;
    }

    private static int CountMatchingTrailingSegments(string[] left, string[] right)
    {
        int count = 0;
        while (count < left.Length
            && count < right.Length
            && string.Equals(
                left[left.Length - count - 1],
                right[right.Length - count - 1],
                StringComparison.OrdinalIgnoreCase))
        {
            count++;
        }

        return count;
    }

    private static string GetAuthority(Uri clusterUri)
    {
        return clusterUri.GetLeftPart(UriPartial.Authority);
    }

    private static XElement? GetElement(XElement parent, string localName)
    {
        return parent.Elements().FirstOrDefault(element => element.Name.LocalName == localName);
    }

    private static string GetFileName(string path)
    {
        string[] segments = GetPathSegments(path);
        return segments.Length == 0 ? string.Empty : segments[^1];
    }

    private static string GetFileNameWithoutExtension(string path)
    {
        string fileName = GetFileName(path);
        int extensionIndex = fileName.LastIndexOf('.');
        return extensionIndex > 0 ? fileName[..extensionIndex] : fileName;
    }

    private static string[] GetPathSegments(string path)
    {
        return path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool IsRecoveryTab(KustoExplorerProfileFile file)
    {
        string[] segments = GetPathSegments(file.RelativePath);
        return segments.Length >= 2
            && string.Equals(segments[^2], RecoveryDirectoryName, StringComparison.OrdinalIgnoreCase)
            && segments[^1].EndsWith(".kebak", StringComparison.OrdinalIgnoreCase);
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

    private static void ResolveQueryTarget(
        string context,
        out Uri? clusterUri,
        out string? databaseName)
    {
        clusterUri = null;
        databaseName = null;
        DbConnectionStringBuilder builder = new();
        bool parsed = true;
        object? sourceValue = null;
        object? databaseValue = null;
        Uri? parsedUri = null;

        try
        {
            builder.ConnectionString = context;
        }
        catch (ArgumentException)
        {
            parsed = false;
        }

        bool valid = parsed
            && builder.TryGetValue("Data Source", out sourceValue)
            && builder.TryGetValue("Initial Catalog", out databaseValue)
            && Uri.TryCreate(Convert.ToString(sourceValue, CultureInfo.InvariantCulture), UriKind.Absolute, out parsedUri)
            && parsedUri.Scheme == Uri.UriSchemeHttps
            && !string.IsNullOrWhiteSpace(parsedUri.Host)
            && !string.IsNullOrWhiteSpace(Convert.ToString(databaseValue, CultureInfo.InvariantCulture));

        if (valid)
        {
            clusterUri = new Uri(parsedUri!.GetLeftPart(UriPartial.Authority), UriKind.Absolute);
            databaseName = Convert.ToString(databaseValue, CultureInfo.InvariantCulture)!.Trim();
        }
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
            connection = new KustoClusterConnection(
                clusterUri!,
                string.IsNullOrWhiteSpace(displayName) ? clusterUri!.Host : displayName,
                [],
                folderName);
        }

        return valid;
    }

    private static bool TryCreateImportedTab(
        string json,
        out KustoExplorerImportedTab importedTab)
    {
        importedTab = null!;

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            Guid id = Guid.Empty;
            int order = 0;
            bool hasText = TryGetJsonString(root, "QueryText", out string text);
            bool valid = root.ValueKind == JsonValueKind.Object
                && GetJsonBoolean(root, "IsOpen")
                && TryGetJsonGuid(root, "Id", out id)
                && hasText
                && text.Length <= MaximumQueryLength
                && TryGetNonnegativeJsonInt32(root, "TabOrdinal", out order);

            if (!valid)
            {
                return false;
            }

            _ = TryGetJsonString(root, "CustomTitle", out string customTitle);
            _ = TryGetJsonString(root, "FullPath", out string fullPath);
            _ = TryGetJsonString(root, "ConnectionString", out string context);
            _ = TryGetJsonString(root, "ColorTag", out string colorTag);
            ResolveQueryTarget(context, out Uri? clusterUri, out string? databaseName);
            importedTab = new KustoExplorerImportedTab(
                id,
                GetImportedTabTitle(customTitle, fullPath, order),
                text,
                Math.Clamp(GetJsonInt32(root, "Position"), 0, text.Length),
                order,
                ParseTabColor(colorTag),
                clusterUri,
                databaseName);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetClusterUri(string serializedConnection, out Uri? clusterUri)
    {
        clusterUri = null;
        string connectionString = serializedConnection.Trim();
        XDocument? connectionDocument = TryLoadXml(serializedConnection);
        if (connectionDocument is not null)
        {
            connectionString = connectionDocument.Root?.Value.Trim() ?? string.Empty;
        }

        DbConnectionStringBuilder builder = new();
        object? sourceValue = null;
        Uri? parsedUri = null;
        try
        {
            builder.ConnectionString = connectionString;
        }
        catch (ArgumentException)
        {
            return false;
        }

        bool valid = builder.TryGetValue("Data Source", out sourceValue)
            && Uri.TryCreate(Convert.ToString(sourceValue, CultureInfo.InvariantCulture), UriKind.Absolute, out parsedUri)
            && parsedUri.Scheme == Uri.UriSchemeHttps
            && !string.IsNullOrWhiteSpace(parsedUri.Host);
        if (valid)
        {
            clusterUri = new Uri(parsedUri!.GetLeftPart(UriPartial.Authority), UriKind.Absolute);
        }

        return valid;
    }

    private static XDocument? TryLoadXml(string content)
    {
        try
        {
            using StringReader textReader = new(content);
            XmlReaderSettings settings = new()
            {
                DtdProcessing = DtdProcessing.Prohibit,
                MaxCharactersInDocument = MaximumXmlCharacterCount,
                XmlResolver = null,
            };
            using XmlReader reader = XmlReader.Create(textReader, settings);
            return XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException)
        {
            return null;
        }
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

    private static string GetImportedTabTitle(string customTitle, string fullPath, int order)
    {
        string title = customTitle.Trim();
        if (title.Length == 0 && fullPath.Length > 0)
        {
            title = GetFileNameWithoutExtension(fullPath.Trim());
        }

        return title.Length == 0 ? $"Imported tab {order + 1:N0}" : title;
    }

    private static bool TryGetJsonGuid(JsonElement root, string propertyName, out Guid value)
    {
        value = Guid.Empty;
        return TryGetJsonString(root, propertyName, out string text)
            && Guid.TryParse(text, out value)
            && value != Guid.Empty;
    }

    private static bool TryGetJsonString(JsonElement root, string propertyName, out string value)
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

    private sealed class ConnectionSource
    {
        internal ConnectionSource(KustoExplorerProfileFile file, string? folderName)
        {
            File = file;
            FolderName = string.IsNullOrWhiteSpace(folderName) ? null : folderName.Trim();
        }

        internal KustoExplorerProfileFile File { get; }

        internal string? FolderName { get; }
    }
}
